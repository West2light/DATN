using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Events;

public class Bullet : MonoBehaviour
{
    public BulletData bulletData;

    [Tooltip("Các lớp mà đạn được phép va chạm. Để trống = tự động dùng các lớp vật cản + xe tăng.")]
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
    }

    private void Update()
    {
        Vector2 currentPosition = transform.position;
        RaycastHit2D hit = Physics2D.Linecast(previousPosition, currentPosition, hitDetectionMask);
        if (hit.collider != null && !ShouldIgnore(hit.collider))
        {
            OnTriggerEnter2D(hit.collider);
            return;
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
        var damagable = collision.GetComponent<Damagable>();
        if (damagable != null)
        {
            damagable.Hit(bulletData.damage);
        }

        DisableObject();
    }
}
