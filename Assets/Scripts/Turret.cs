using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Events;

[RequireComponent(typeof(ObjectPool))]
public class Turret : MonoBehaviour
{
    private static readonly List<float> RecentEnemyShotTimes = new List<float>();

    public List<Transform> turretBarrels;

    public TurretData turretData;

    private bool canShoot = true;
    private Collider2D[] tankColliders;
    private float currentDelay = 0;

    private ObjectPool bulletPool;
    [SerializeField]
    private int bulletPoolCount = 10;
    [SerializeField, Range(0f, 1f)] private float enemyShotBaseVolume = 0.11f;
    [SerializeField, Range(0.1f, 3f)] private float enemyShotBasePitch = 0.78f;
    [SerializeField, Range(0f, 0.5f)] private float enemyShotPitchRandomRange = 0.05f;
    [SerializeField, Min(0.01f)] private float enemyShotBurstWindowSeconds = 0.3f;
    [SerializeField, Range(0.1f, 1f)] private float enemyShotBurstVolumeMultiplier = 0.72f;
    [SerializeField, Min(0f)] private float enemyShotNearDuplicateWindowSeconds = 0.04f;

    // Gốc xe tăng sở hữu tháp pháo này — đạn bắn ra sẽ bỏ qua mọi collider của nó.
    private Transform tankRoot;

    public UnityEvent OnShoot, OnCantShoot;
    public UnityEvent<float> OnReloading;

    private void Awake()
    {
        tankColliders = GetComponentsInParent<Collider2D>();
        bulletPool = GetComponent<ObjectPool>();

        TankController tankController = GetComponentInParent<TankController>();
        tankRoot = tankController != null ? tankController.transform : transform.root;
    }

    private void Start()
    {
        bulletPool.Initialize(turretData.bulletPrefab, bulletPoolCount);
        OnReloading?.Invoke(currentDelay);
    }

    private void Update()
    {
        if (canShoot == false)
        {
            currentDelay -= Time.deltaTime;
            OnReloading?.Invoke(currentDelay/ turretData.reloadDelay);
            if (currentDelay <= 0)
            {
                canShoot = true;
            }
        }
    }

    public void Shoot()
    {
        if (canShoot)
        {
            canShoot = false;
            currentDelay = turretData.reloadDelay;

            foreach (var barrel in turretBarrels)
            {
                GameObject bullet = bulletPool.CreateObject();
                bullet.transform.position = barrel.position;
                bullet.transform.localRotation = barrel.rotation;
                bullet.GetComponent<Bullet>().Initialize(turretData.bulletData, tankRoot);

                foreach (var collider in tankColliders)
                {
                    Physics2D.IgnoreCollision(bullet.GetComponent<Collider2D>(), collider);
                }

            }

            PrepareShootAudio();
            OnShoot?.Invoke();
            OnReloading?.Invoke(currentDelay);
        }
        else
        {
            OnCantShoot?.Invoke();
        }

    }

    private void PrepareShootAudio()
    {
        FactionMember factionMember = tankRoot != null ? tankRoot.GetComponentInParent<FactionMember>() : null;
        if (factionMember == null || factionMember.CurrentFaction != Faction.Enemy)
        {
            return;
        }

        float now = Time.unscaledTime;
        for (int i = RecentEnemyShotTimes.Count - 1; i >= 0; i--)
        {
            if (now - RecentEnemyShotTimes[i] > enemyShotBurstWindowSeconds)
            {
                RecentEnemyShotTimes.RemoveAt(i);
            }
        }

        bool nearDuplicate = false;
        for (int i = 0; i < RecentEnemyShotTimes.Count; i++)
        {
            if (now - RecentEnemyShotTimes[i] <= enemyShotNearDuplicateWindowSeconds)
            {
                nearDuplicate = true;
                break;
            }
        }

        float volume = enemyShotBaseVolume * Mathf.Pow(enemyShotBurstVolumeMultiplier, RecentEnemyShotTimes.Count);
        if (nearDuplicate)
        {
            volume *= 0.35f;
        }

        AudioSource[] audioSources = GetComponentsInChildren<AudioSource>(true);
        for (int i = 0; i < audioSources.Length; i++)
        {
            AudioSource source = audioSources[i];
            if (source == null)
            {
                continue;
            }

            source.volume = Mathf.Clamp01(volume);
            source.pitch = enemyShotBasePitch + Random.Range(-enemyShotPitchRandomRange, enemyShotPitchRandomRange);
            source.spatialBlend = 0f;
        }

        RecentEnemyShotTimes.Add(now);
    }
}
