﻿#define DEBUG_CHARACTER_RUN

using System.Collections;
using System.Collections.Generic;
using UnityEngine;

using CommonsDebug;
using CommonsHelper;

public class CharacterRun : MonoBehaviour
{
    /* Static member for unique raycast allocations for CharacterRun scripts */
    private static readonly RaycastHit2D[] RaycastHits = new RaycastHit2D[2];
    
    /* Child references */
    [Tooltip("Position X from which ground is detected. Y must be at feet level. Place it slightly behind the character to allow last second jump. Actual raycast Y range is defined by Ground Detection Start/Stop Offset parameters.")]
    public Transform groundSensorXTr;
    
    /* Sibling components */
    private Rigidbody2D m_Rigidbody2D;
    private BoxCollider2D m_Collider2D;
    private FlagBearer m_FlagBearer;
    
    /* Parameters */
    
    [SerializeField, Tooltip("Player number (1 or 2)")]
    private int playerNumber = 0;
    public int PlayerNumber => playerNumber;

    [SerializeField, Tooltip("Ground raycast contact filter")]
    private ContactFilter2D groundFilter = default;

    [SerializeField, Tooltip("Distance above feet to detect ground (must be at least expected penetration depth)")]
    private float groundDetectionStartMargin = 0.1f;

    [SerializeField, Tooltip("Distance below feet to stop detecting ground. Must be positive to at least detect ground at feet level, but a very small margin is enough. It is only to avoid missing ground just at the tip of a raycast, as ground detected more below will be ignored anyway.")]
    private float groundDetectionStopMargin = 0.1f;

    [SerializeField, Tooltip("Distance between feet and ground under which we consider character to be just touching ground, so no Y adjustment is applied. This is used for both penetration tolerance (small positive distance sensed) and hover tolerance (small negative distance sensed). This prevents oscillations between Landing and Falling as the feet Y wouldn't exactly match ground Y due to floating precision. Should be lower than margins above.")]
    private float groundDetectionToleranceHalfRange = 0.01f;

    [SerializeField, Tooltip("Run speed X at normal pace")]
    private float baseRunSpeed = 10f;
    
    [SerializeField, Tooltip("Factor applied to run speed X when slowed down by an obstacle")]
    private float obstacleSlowDownRunSpeedMultiplier = 0.7f;
    
    [SerializeField, Tooltip("Factor applied to run speed X when holding the flag")]
    private float flagSlowDownRunSpeedMultiplier = 0.7f;
    
    [SerializeField, Tooltip("Jump speed Y")]
    private float jumpSpeed = 10f;
    
    [SerializeField, Tooltip("Gravity acceleration (positive downward)")]
    private float gravity = 10f;

    [SerializeField, Tooltip("Slowdown duration on obstacle hit")]
    private float obstacleSlowdownDuration = 1f;

    /* State vars */
    
    /// Current state
    private CharacterState m_State = CharacterState.WaitForStart;

    /// Obstacle slow down timer. When positive, character is currently slowed down by the last obstacle hurt.
    private float m_ObstacleSlowDownTimer = 0f;
    
    private void Awake()
    {
        m_Rigidbody2D = this.GetComponentOrFail<Rigidbody2D>();
        m_Collider2D = this.GetComponentOrFail<BoxCollider2D>();
        m_FlagBearer = this.GetComponentOrFail<FlagBearer>();
    }

    public void StartRunning()
    {
        m_State = CharacterState.Run;
    }

    private void FixedUpdate()
    {
        // don't move before race start
        if (m_State == CharacterState.WaitForStart)
        {
            return;
        }
        
        // update slowdown timer
        if (m_ObstacleSlowDownTimer > 0f)
        {
            m_ObstacleSlowDownTimer = Mathf.Max(0f, m_ObstacleSlowDownTimer - Time.deltaTime);
        }
            
        // Transitions
        
        if (m_State == CharacterState.Run)
        {
            // sense ground to check if no ground anymore
            // for now, we ignore the result groundDistance if ground is detected
            //  as we assume that all platforms are flat and we don't need to escape ground while running
            if (!SenseGround(out var groundDistance))
            {
                // no ground detected, fall (we can wait next frame to start applying gravity)
                m_State = CharacterState.Fall;
#if DEBUG_CHARACTER_RUN
                Debug.LogFormat(this, "[CharacterRun] #{0} No ground detected, Fall", playerNumber);
#endif
            }
        }
        else
        {
            // airborne
            switch (m_State)
            {
                case CharacterState.Jump:
                {
                    // sense ground when character starts falling
                    if (m_Rigidbody2D.velocity.y < 0f && SenseGround(out var groundDistance))
                    {
                        // detected ground, leave Jump, adjust position Y to ground and reset velocity Y
                        m_State = CharacterState.Run;
                        Land(groundDistance);
#if DEBUG_CHARACTER_RUN
                        Debug.LogFormat(this, "[CharacterRun] #{0} Ground detected, Land at groundDistance: {1}", playerNumber, groundDistance);
#endif
                    }
                    break;
                }
                case CharacterState.Fall:
                {
                    // we assume falling character is moving downward so always sense ground in this state
                    if (SenseGround(out var groundDistance))
                    {
                        // detected ground, leave Fall, adjust position Y to ground and reset velocity Y
                        m_State = CharacterState.Run;
                        Land(groundDistance);
#if DEBUG_CHARACTER_RUN
                        Debug.LogFormat(this, "[CharacterRun] #{0} Ground detected, Land at groundDistance: {0}", playerNumber, groundDistance);
#endif
                    }
                    break;
                }
            }
        }
        
        // After doing all transitions, set velocity based on the resulting state
        float runSpeed = GetSlowDownMultiplier() * baseRunSpeed;
        
        if (m_State == CharacterState.Run)
        {
            // run horizontally
            m_Rigidbody2D.velocity = runSpeed * Vector2.right;
        }
        else  // airborne
        {
            // apply gravity
            m_Rigidbody2D.velocity = new Vector2(runSpeed, m_Rigidbody2D.velocity.y - gravity * Time.deltaTime);
        }
    }

    /// Compute and return slowdown multiplier from current state
    private float GetSlowDownMultiplier()
    {
        float slowDownMultiplier = 1f;
        if (m_ObstacleSlowDownTimer > 0f) slowDownMultiplier *= obstacleSlowDownRunSpeedMultiplier;
        if (m_FlagBearer.HasFlag()) slowDownMultiplier *= flagSlowDownRunSpeedMultiplier;
        return slowDownMultiplier;
    }
    
    /// Sense ground slightly above or at feet level and return true iff ground is detected,
    /// along with out distance to it, positive upward.
    /// Out distance should be positive or zero, zero if just touching ground and positive if slightly above.
    /// If ground is too high or below feet, no ground is detected, return false with outDistance 0 (unused).
    private bool SenseGround(out float outGroundDistance)
    {
        Vector2 origin = (Vector2) groundSensorXTr.position + groundDetectionStartMargin * Vector2.up;
        // make sure to compute sum of start and stop margin, not difference,
        // as the former is upward, the latter backward
        int hitCount = Physics2DUtil.RaycastDebug(origin, Vector2.down, groundFilter, RaycastHits,
            groundDetectionStartMargin + groundDetectionStopMargin);
        if (hitCount > 0)
        {
            // only consider first hit
            float hitDistance = RaycastHits[0].distance;
            outGroundDistance = groundDetectionStartMargin - hitDistance;

            if (Mathf.Abs(outGroundDistance) <= groundDetectionToleranceHalfRange)
            {
                // we're close enough to ground (slightly inside or above)
                // to consider we are grounded and don't need any offset (prevents oscillation around ground Y)
                outGroundDistance = 0f;
                return true;
            }
            else if (outGroundDistance >= 0)  // actually > groundDetectionToleranceHalfRange
            {
                // we are inside the ground by a meaningful distance
                return true;
            }
            else  // outGroundDistance < - groundDetectionToleranceHalfRange
            {
                // we are above ground by a meaningful distance
                return false;
            }
        }

        outGroundDistance = 0f;  // just because we need something for out
        return false;
    }
    
    /// Adjust position to land on ground located at groundDistance, positive upward
    /// This method does not set the state to Run, do it separately
    private void Land(float groundDistance)
    {
        // after MovePosition, physics velocity is not normally applied,
        //  so to avoid lagging by 1 frame on X on landing, we manually inject dx on landing frame
        m_Rigidbody2D.MovePosition(m_Rigidbody2D.position +
                                   new Vector2(m_Rigidbody2D.velocity.x * Time.deltaTime, groundDistance));
        m_Rigidbody2D.velocity = new Vector2(m_Rigidbody2D.velocity.x, 0f);
    }

    /// Start obstacle slow down timer
    /// Leave obstacle destruction to Obstacle script side
    /// If you already hit an obstacle and timer is still active, just reset it to duration
    public void CrashIntoObstacle()
    {
        m_ObstacleSlowDownTimer = obstacleSlowdownDuration;
    }
    
    public void PlayToggleSwitchAnim()
    {
        // TODO
    }
    
    public void PlayFinishAnim()
    {
        // TODO
    }
    
    /// Input callback: Jump action
    private void OnJump()
    {
        if (m_State == CharacterState.Run)
        {
            m_State = CharacterState.Jump;
            m_Rigidbody2D.velocity += jumpSpeed * Vector2.up;
            
#if DEBUG_CHARACTER_RUN
            Debug.LogFormat(this, "[CharacterRun] #{0} Jump with jumpSpeed: {1}", playerNumber, jumpSpeed);
#endif
        }
    }
}
