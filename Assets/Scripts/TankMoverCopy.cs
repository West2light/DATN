using UnityEngine;
using UnityEngine.Events;

public class TankMoverCopy : MonoBehaviour
{
    public Rigidbody2D rb2d;
    public TankMovementData movementData;
    [Min(0.1f)] public float defaultRotationToleranceDeg = 2f;

    private Vector2 movementVector;
    private Vector2 worldMovementVector;
    private bool useWorldMovement;
    private float currentSpeed = 0f;
    private float currentForwardDirection = 1f;
    private bool hasRotationTarget;
    private float rotationTargetAngle;
    private float rotationMaxDegreesPerSecond;
    private float rotationToleranceDeg;

    public UnityEvent<float> OnSpeedChange = new UnityEvent<float>();

    private void Awake()
    {
        rb2d = GetComponentInParent<Rigidbody2D>();
        if (rb2d != null)
            rb2d.constraints |= RigidbodyConstraints2D.FreezeRotation;
    }

    public void Move(Vector2 movementVector)
    {
        hasRotationTarget = false;
        useWorldMovement = false;
        this.movementVector = movementVector;
        CalculateSpeed(movementVector);
        OnSpeedChange?.Invoke(this.movementVector.magnitude);

        if (movementVector.y > 0f)
        {
            if (currentForwardDirection == -1f)
                currentSpeed = 0f;
            currentForwardDirection = 1f;
        }
        else if (movementVector.y < 0f)
        {
            if (currentForwardDirection == 1f)
                currentSpeed = 0f;
            currentForwardDirection = -1f;
        }
    }

    public void MoveWorldDirection(Vector2 direction)
    {
        hasRotationTarget = false;
        useWorldMovement = true;
        worldMovementVector = direction.sqrMagnitude > 1f ? direction.normalized : direction;
        movementVector = Vector2.zero;
        currentSpeed = 0f;
        OnSpeedChange?.Invoke(worldMovementVector.magnitude);
    }

    public void BeginRotateToAngle(float targetAngle, float maxDegPerSecond, float toleranceDeg)
    {
        hasRotationTarget = true;
        rotationTargetAngle = targetAngle;
        rotationMaxDegreesPerSecond = Mathf.Max(1f, maxDegPerSecond);
        rotationToleranceDeg = Mathf.Max(0.1f, toleranceDeg);
        useWorldMovement = false;
        movementVector = Vector2.zero;
        worldMovementVector = Vector2.zero;
        currentSpeed = 0f;
        OnSpeedChange?.Invoke(0f);
    }

    public void CancelRotationTarget()
    {
        hasRotationTarget = false;
    }

    public void StopBodyMovement(bool preserveRotationTarget = false)
    {
        if (!preserveRotationTarget)
            hasRotationTarget = false;

        useWorldMovement = false;
        movementVector = Vector2.zero;
        worldMovementVector = Vector2.zero;
        currentSpeed = 0f;
        OnSpeedChange?.Invoke(0f);
    }

    public bool IsRotationTargetComplete()
    {
        return !hasRotationTarget || Mathf.Abs(Mathf.DeltaAngle(GetCurrentRotationAngle(), rotationTargetAngle)) <= rotationToleranceDeg;
    }

    public float GetRotationDeltaToTarget()
    {
        return hasRotationTarget ? Mathf.DeltaAngle(GetCurrentRotationAngle(), rotationTargetAngle) : 0f;
    }

    private void CalculateSpeed(Vector2 movementVector)
    {
        if (movementData == null)
        {
            currentSpeed = 0f;
            return;
        }

        if (Mathf.Abs(movementVector.y) > 0f)
            currentSpeed += movementData.acceleration * Time.deltaTime;
        else
            currentSpeed -= movementData.deacceleration * Time.deltaTime;

        currentSpeed = Mathf.Clamp(currentSpeed, 0f, movementData.maxSpeed);
    }

    private void FixedUpdate()
    {
        if (rb2d == null || movementData == null)
            return;

        rb2d.angularVelocity = 0f;

        if (hasRotationTarget)
        {
            rb2d.linearVelocity = Vector2.zero;
            float nextAngle = Mathf.MoveTowardsAngle(
                rb2d.rotation,
                rotationTargetAngle,
                rotationMaxDegreesPerSecond * Time.fixedDeltaTime);
            rb2d.MoveRotation(nextAngle);

            if (Mathf.Abs(Mathf.DeltaAngle(nextAngle, rotationTargetAngle)) <= rotationToleranceDeg)
            {
                rb2d.MoveRotation(rotationTargetAngle);
                hasRotationTarget = false;
            }

            return;
        }

        if (useWorldMovement)
        {
            rb2d.linearVelocity = worldMovementVector * movementData.maxSpeed;

            if (worldMovementVector.sqrMagnitude > 0.001f)
            {
                float desiredAngle = Mathf.Atan2(worldMovementVector.y, worldMovementVector.x) * Mathf.Rad2Deg - 90f;
                Quaternion targetRotation = Quaternion.Euler(0f, 0f, desiredAngle);
                rb2d.rotation = Quaternion.RotateTowards(
                    transform.rotation,
                    targetRotation,
                    movementData.rotationSpeed * Time.fixedDeltaTime).eulerAngles.z;
            }

            return;
        }

        rb2d.linearVelocity = (Vector2)transform.up * currentSpeed * currentForwardDirection;
        rb2d.rotation = (transform.rotation * Quaternion.Euler(0f, 0f, -movementVector.x * movementData.rotationSpeed * Time.fixedDeltaTime)).eulerAngles.z;
    }

    private float GetCurrentRotationAngle()
    {
        if (rb2d != null)
            return rb2d.rotation;
        return transform.eulerAngles.z;
    }
}
