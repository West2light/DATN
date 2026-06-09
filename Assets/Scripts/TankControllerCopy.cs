using UnityEngine;

public class TankControllerCopy : MonoBehaviour
{
    public TankMoverCopy tankMover;
    public AimTurret aimTurret;
    public Turret[] turrets;

    private void Awake()
    {
        if (tankMover == null)
            tankMover = GetComponentInChildren<TankMoverCopy>(true);
        if (aimTurret == null)
            aimTurret = GetComponentInChildren<AimTurret>(true);
        if (turrets == null || turrets.Length == 0)
            turrets = GetComponentsInChildren<Turret>(true);
    }

    public void HandleShoot()
    {
        if (turrets == null)
            return;

        foreach (Turret turret in turrets)
        {
            if (turret != null)
                turret.Shoot();
        }
    }

    public void HandleMoveBody(Vector2 movementVector)
    {
        if (tankMover != null)
            tankMover.Move(movementVector);
    }

    public void HandleMoveWorldDirection(Vector2 movementVector)
    {
        if (tankMover != null)
            tankMover.MoveWorldDirection(movementVector);
    }

    public void StopBodyMovement(bool preserveRotationTarget = false)
    {
        if (tankMover != null)
            tankMover.StopBodyMovement(preserveRotationTarget);
    }

    public void HandleTurretMovement(Vector2 pointerPosition)
    {
        if (aimTurret != null)
            aimTurret.Aim(pointerPosition);
    }
}
