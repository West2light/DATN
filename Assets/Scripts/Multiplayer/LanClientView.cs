using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// CLIENT-ONLY singleton in the game scene.
/// Receives state from the server and updates lightweight ghost GameObjects.
/// Camera from MapTankTestBootstrap is pointed at _ownGhost so the client
/// always follows their own tank.
/// </summary>
public class LanClientView : MonoBehaviour
{
    public static LanClientView Instance { get; private set; }

    // The ghost that represents THIS client's own tank (camera follows it)
    public Transform OwnGhost { get; private set; }

    private readonly List<Transform> _playerGhosts = new List<Transform>();
    private readonly List<Transform> _enemyGhosts  = new List<Transform>();
    private int _ownSlot = -1;

    private void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
    }

    private void OnDestroy()
    {
        if (Instance == this) Instance = null;
    }

    // ── Called by MapTankTestBootstrap (clients) to set up ghosts ─────────────

    public void InitGhosts(int playerCount, int enemyCount)
    {
        // Determine own slot by finding this client's bridge
        var bridges = FindObjectsByType<LanNetworkBridge>(FindObjectsSortMode.None);
        foreach (var b in bridges)
        {
            if (b.IsOwner)
            {
                _ownSlot = b.Slot.Value >= 0 ? b.Slot.Value : 0;
                break;
            }
        }

        // Player ghosts (colored squares)
        Color[] slotColors = { Color.cyan, Color.yellow, Color.magenta, Color.green,
                               Color.white, Color.red, new Color(1f, 0.5f, 0f), Color.blue };

        for (int i = 0; i < playerCount; i++)
        {
            Color col = i < slotColors.Length ? slotColors[i] : Color.white;
            Transform ghost = CreateGhost($"GhostPlayer_{i}", col, 0.6f);
            _playerGhosts.Add(ghost);
            if (i == _ownSlot) OwnGhost = ghost;
        }

        // Enemy ghosts (dark red squares)
        for (int i = 0; i < enemyCount; i++)
        {
            Transform ghost = CreateGhost($"GhostEnemy_{i}", new Color(0.8f, 0.1f, 0.1f), 0.5f);
            _enemyGhosts.Add(ghost);
        }
    }

    private static Transform CreateGhost(string name, Color color, float size)
    {
        var go  = new GameObject(name);
        var sr  = go.AddComponent<SpriteRenderer>();
        sr.sprite = CreateSquareSprite();
        sr.color  = color;
        sr.sortingLayerName = "Agent";
        go.transform.localScale = Vector3.one * size;
        return go.transform;
    }

    private static Sprite CreateSquareSprite()
    {
        var tex = new Texture2D(1, 1, TextureFormat.RGBA32, false);
        tex.SetPixel(0, 0, Color.white);
        tex.Apply(false, true);
        return Sprite.Create(tex, new Rect(0, 0, 1, 1), Vector2.one * 0.5f, 1f);
    }

    // ── Receive own tank state (position for camera follow) ───────────────────

    public void ApplyOwnTankState(LanTankState state)
    {
        if (OwnGhost == null) return;
        OwnGhost.position   = state.pos;
        OwnGhost.rotation   = Quaternion.Euler(0f, 0f, state.bodyRot);
    }

    // ── Receive full world snapshot ───────────────────────────────────────────

    public void ApplyWorldState(LanTankState[] playerStates, LanEnemyState[] enemyStates, int eagleHp)
    {
        // Update other player ghosts
        for (int i = 0; i < playerStates.Length && i < _playerGhosts.Count; i++)
        {
            if (i == _ownSlot) continue; // own tank handled via ApplyOwnTankState
            Transform ghost = _playerGhosts[i];
            if (ghost == null) continue;
            ghost.position = playerStates[i].pos;
            ghost.rotation = Quaternion.Euler(0f, 0f, playerStates[i].bodyRot);
        }

        // Update enemy ghosts
        for (int i = 0; i < enemyStates.Length; i++)
        {
            int idx = enemyStates[i].idx;
            if (idx < 0 || idx >= _enemyGhosts.Count) continue;
            Transform ghost = _enemyGhosts[idx];
            if (ghost == null) continue;

            if (enemyStates[i].hp <= 0)
            {
                ghost.gameObject.SetActive(false);
            }
            else
            {
                ghost.gameObject.SetActive(true);
                ghost.position = enemyStates[i].pos;
                ghost.rotation = Quaternion.Euler(0f, 0f, enemyStates[i].rot);
            }
        }
    }

    // ── Receive win / game over event ─────────────────────────────────────────

    public void ApplyGameEvent(bool isWin)
    {
        if (isWin)
            MapWinController.Ensure().BeginWin();
        else
            MapGameOverController.Ensure().BeginGameOver();
    }
}
