using UnityEngine;

public enum Faction
{
    Neutral = 0,
    Player = 1,
    Enemy = 2,
    Base = 3
}

public class FactionMember : MonoBehaviour
{
    [SerializeField] private Faction faction = Faction.Neutral;

    public Faction CurrentFaction
    {
        get => faction;
        set => faction = value;
    }

    public static FactionMember Ensure(GameObject target, Faction faction)
    {
        FactionMember member = target.GetComponent<FactionMember>();
        if (member == null)
        {
            member = target.AddComponent<FactionMember>();
        }

        member.CurrentFaction = faction;
        return member;
    }

    public static FactionMember FindForCollider(Collider2D collider)
    {
        if (collider == null)
        {
            return null;
        }

        return collider.GetComponentInParent<FactionMember>();
    }

    public static bool AreFriendly(FactionMember a, FactionMember b)
    {
        if (a == null || b == null)
        {
            return false;
        }

        if (a.CurrentFaction == Faction.Neutral || b.CurrentFaction == Faction.Neutral)
        {
            return false;
        }

        return a.CurrentFaction == b.CurrentFaction;
    }

    public Vector3 GetWorldPosition()
    {
        TankController tankController = GetComponentInChildren<TankController>();
        if (tankController != null && tankController.tankMover != null)
        {
            return tankController.tankMover.transform.position;
        }

        Rigidbody2D body = GetComponentInChildren<Rigidbody2D>();
        if (body != null)
        {
            return body.position;
        }

        return transform.position;
    }
}
