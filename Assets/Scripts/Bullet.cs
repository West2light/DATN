using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Events;

public class Bullet : MonoBehaviour
{
    // Fired on the SERVER whenever any bullet is created. LanGameCoordinator uses this
    // to immediately RPC clients so they see bullets without positional-sync delay.
    public static System.Action<Vector2, Vector2, float, float> OnAnyBulletFired; // pos, dir, speed, maxDist

    // Fired on the SERVER whenever any bullet hits something. LanGameCoordinator relays
    // this to clients so they see the explosion effect at the correct world position.
    public static System.Action<Vector2> OnAnyBulletHit; // world position of impact

    public BulletData bulletData;

    [Tooltip("Layers that bullets may collide with. Leave empty to automatically use obstacle and tank layers.")]
    public LayerMask hitDetectionMask;

    private Vector2 startPosition;
    private float conquaredDistance = 0;
    private Rigidbody2D rb2d;
    private Vector2 previousPosition;
    private Transform owner;   // xe tăng bắn ra viên đạn — đạn luôn bỏ qua mọi collider của nó
    private FactionMember ownerFaction;

    public UnityEvent OnHit = new UnityEvent();

    private void Awake()
    {
        rb2d = GetComponent<Rigidbody2D>();
        if (hitDetectionMask.value == 0)
        {
            hitDetectionMask = LayerMask.GetMask(
                "Hittable", "Agent", "Enemy", "Player", "Walls", "ObstaclesMovement");
        }
    }

    public void Initialize(BulletData bulletData) => Initialize(bulletData, null);

    public void Initialize(BulletData bulletData, Transform owner)
    {
        this.bulletData = bulletData;
        this.owner = owner;
        ownerFaction = owner != null ? owner.GetComponentInParent<FactionMember>() : null;
        startPosition = transform.position;
        previousPosition = transform.position;
        rb2d.linearVelocity = transform.up * this.bulletData.speed;
        OnAnyBulletFired?.Invoke(transform.position, transform.up,
            this.bulletData.speed, this.bulletData.maxDistance);
    }

    private void Update()
    {
        Vector2 currentPosition = transform.position;
        RaycastHit2D[] hits = Physics2D.LinecastAll(previousPosition, currentPosition, hitDetectionMask);
        foreach (var hit in hits)
        {
            if (hit.collider != null && !ShouldIgnore(hit.collider))
            {
                OnTriggerEnter2D(hit.collider);
                return;
            }
        }

        previousPosition = currentPosition;
        conquaredDistance = Vector2.Distance(transform.position, startPosition);
        if (conquaredDistance >= bulletData.maxDistance)
        {
            DisableObject();
        }
    }

    private void DisableObject()
    {
        rb2d.linearVelocity = Vector2.zero;
        gameObject.SetActive(false);
    }

    // Đạn bỏ qua chính nó và toàn bộ collider của xe tăng đã bắn ra nó.
    // Player bullets stop at the Eagle Base but deal no damage (only enemy agents can destroy it).
    private bool IsPlayerHittingBase(Collider2D col)
    {
        if (ownerFaction == null || ownerFaction.CurrentFaction != Faction.Player) return false;
        FactionMember target = FactionMember.FindForCollider(col);
        return target != null && target.CurrentFaction == Faction.Base;
    }

    private bool ShouldIgnore(Collider2D col)
    {
        if (col.gameObject == gameObject)
        {
            return true;
        }

        if (owner != null && col.transform.IsChildOf(owner))
        {
            return true;
        }

        FactionMember targetFaction = FactionMember.FindForCollider(col);
        return FactionMember.AreFriendly(ownerFaction, targetFaction);
    }

    private void OnTriggerEnter2D(Collider2D collision)
    {
        if (ShouldIgnore(collision))
        {
            return;
        }

        // Bỏ qua collider không thuộc lớp được phép (player-blocker, đạn khác, ...).
        if ((hitDetectionMask.value & (1 << collision.gameObject.layer)) == 0)
        {
            return;
        }

        OnHit?.Invoke();
        OnAnyBulletHit?.Invoke(transform.position);
        var damagable = collision.GetComponentInParent<Damagable>();
        if (damagable != null && !IsPlayerHittingBase(collision))
        {
            damagable.Hit(bulletData.damage);
        }

        DisableObject();
    }
}
