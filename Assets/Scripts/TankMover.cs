using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Events;

public class TankMover : MonoBehaviour
{
    public Rigidbody2D rb2d;

    public TankMovementData movementData;

    private Vector2 movementVector;
    private Vector2 worldMovementVector;
    private bool useWorldMovement;
    private float currentSpeed = 0;
    private float currentForewardDirection = 1;

    public UnityEvent<float> OnSpeedChange = new UnityEvent<float>();

    private void Awake()
    {
        rb2d = GetComponentInParent<Rigidbody2D>();
    }

    public void Move(Vector2 movementVector)
    {
        useWorldMovement = false;
        this.movementVector = movementVector;
        CalculateSpeed(movementVector);
        OnSpeedChange?.Invoke(this.movementVector.magnitude);
        if (movementVector.y > 0)
        {
            if (currentForewardDirection == -1)
                currentSpeed = 0;
            currentForewardDirection = 1;
        }  
        else if (movementVector.y < 0)
        {
            if (currentForewardDirection == 1)
                currentSpeed = 0;
            currentForewardDirection = -1;
        }
            
    }

    public void MoveWorldDirection(Vector2 direction)
    {
        useWorldMovement = true;
        worldMovementVector = direction.sqrMagnitude > 1f ? direction.normalized : direction;
        movementVector = Vector2.zero;
        currentSpeed = 0f;
        OnSpeedChange?.Invoke(worldMovementVector.magnitude);
    }

    private void CalculateSpeed(Vector2 movementVector)
    {
        if (Mathf.Abs(movementVector.y) > 0)
        {
            currentSpeed += movementData.acceleration * Time.deltaTime;
        }
        else
        {
            currentSpeed -= movementData.deacceleration * Time.deltaTime;
        }
        currentSpeed = Mathf.Clamp(currentSpeed, 0, movementData.maxSpeed);
    }

    private void FixedUpdate()
    {
        if (useWorldMovement)
        {
            rb2d.linearVelocity = worldMovementVector * movementData.maxSpeed * Time.fixedDeltaTime;

            if (worldMovementVector.sqrMagnitude > 0.001f)
            {
                float desiredAngle = Mathf.Atan2(worldMovementVector.y, worldMovementVector.x) * Mathf.Rad2Deg - 90f;
                Quaternion targetRotation = Quaternion.Euler(0f, 0f, desiredAngle);
                rb2d.MoveRotation(Quaternion.RotateTowards(
                    transform.rotation,
                    targetRotation,
                    movementData.rotationSpeed * Time.fixedDeltaTime));
            }

            return;
        }

        rb2d.linearVelocity = (Vector2)transform.up * currentSpeed * currentForewardDirection * Time.fixedDeltaTime;
        rb2d.MoveRotation(transform.rotation * Quaternion.Euler(0, 0, -movementVector.x * movementData.rotationSpeed * Time.fixedDeltaTime));
    }
}
