using UnityEngine;
using System.Collections;
using System.Collections.Generic;

public class PlayerCharacterController : MonoBehaviour
{
  [SerializeField] private float m_MaxVelocity;
  [SerializeField] private float m_AccelerationRate;
  [SerializeField] private float m_MaxVelocityHuggingWall;
  [SerializeField] private float m_AccelerationRateHuggingWall;
  [SerializeField] [Range(0, 10)] private float m_Friction;
  [SerializeField] private LayerMask m_WallLayers;  // Layermask determining what is a wall to the character
  // Varying friction based on input magnitude allows us to slow down faster when letting go
  [SerializeField] private AnimationCurve m_frictionAtInputMagnitude;
  [SerializeField] private AnimationCurve m_reverseDirectionAngleScalar;
  [SerializeField] private AnimationCurve m_wallSlideInputScalar; // adjustment to input magnitude based on dot product to wall
                                                                  // line when hugging wall
  [SerializeField] private AnimationCurve m_VelocityScalarAfterWallKickoff;
  [SerializeField] private float m_wallHugOffset; // How close to a wall do we snap when hugging?
  [SerializeField] private float m_touchingWallRadius; // Size of collider to touch walls
  [SerializeField] private Vector2 m_leftFootRelativePos; // Location (local space) from which to raycast down to see if foot is
                                         // off the edge of our current wall.
                                         // Right foot is inverse x of this
  [SerializeField] private float m_maxSlopeAdjustDistance; // Distance to cast from our feet position to find a new slope
                                                           // If the floor is farther than this, we won't move any further
  [SerializeField] private bool m_debug; // Enable debug logging, drawing?

  private bool m_TouchingWall;              // Whether or not player is touching environment. Updated every frame
  private List<Collider2D> m_WallsTouching = new List<Collider2D>(); // Container for walls player is touching. Updated every frame
  private Rigidbody2D m_Rigidbody2D;
  private RaycastHit2D[] m_WallsCollided = new RaycastHit2D[k_maxNumberOfWallCollisions];
  private float m_lastTimeSwitchedWalls;
  private float m_lastTimeKickedOffWall;

  private Collider2D m_formerCollider;

  const int k_maxNumberOfWallCollisions = 10;
  const float k_minTimeBetweenSwitchingWalls = .2f;

  private Collider2D m_DEBUG_formerCollider;

  private void Awake()
  {
    // Setting up references -- the SLOW way
    m_Rigidbody2D = GetComponent<Rigidbody2D>();
  }

  private void FixedUpdate()
  {
    SetTouchingWall();
  }

  private void SetTouchingWall()
  { 
    m_TouchingWall = false;

    //// The player is touching the wall if a circlecast
    //// at their posiion hits anything designated as ground
    //m_WallsTouching.Clear();
    //Collider2D[] colliders = Physics2D.OverlapCircleAll( transform.position,
    //                            k_touchingWallRadius, m_WallLayers );
    //for( int i = 0; i < colliders.Length; ++i )
    //{
    //  if( colliders[i].gameObject != gameObject )
    //  {
    //    m_WallsTouching.Add( colliders[i] );
    //    m_TouchingWall = true;
    //  }
    //}

    System.Array.Clear( m_WallsCollided, 0, k_maxNumberOfWallCollisions );
    int collidersHit = Physics2D.CircleCastNonAlloc( transform.position, m_touchingWallRadius,
      Vector2.zero, m_WallsCollided, 0f, m_WallLayers );

    m_TouchingWall = collidersHit > 0;

  }

  /// <summary>
  /// Move is called by an attached [*]Control script,
  /// to allow the same character controller to be used by AIs and Players
  /// </summary>
  /// <param name="moveDir">Direction + magnitude</param>
  public void Move( Vector2 moveDir, bool hugWall, bool jump )
  {
    int targetIndex = 0;
    Vector2 outputVelocity = Vector2.zero;
    float outputVelocityMagnitude;

    // If touching wall, slide along wall's edge
    if( m_TouchingWall && hugWall )
    {
      // If player is touching multiple walls, select the one character is moving towards.
      // We use "facing" because it's a cached 'last direction moving'. If we want to change facing
      // to away from wall, we'll need another system -- cache last movement vector > epsilon, for example
      RaycastHit2D newRaycastToOldCollider;
      bool timeoutForNewWall = (Time.time - m_lastTimeSwitchedWalls) > k_minTimeBetweenSwitchingWalls;

      float minDotProductToCollision = float.MinValue;
      for( int i = 0; i < k_maxNumberOfWallCollisions; ++i )
      {
        if( m_WallsCollided[i].collider == null )
        {
          break;
        }

        // Timeout check before switching walls to avoid flickering between options 
        if( !timeoutForNewWall && m_WallsCollided[i].collider == m_formerCollider )
        {
          newRaycastToOldCollider = m_WallsCollided[i];
          targetIndex = i;
          break;
        }

        // Select desired wall -- closest angle towards input angle
        Vector2 vecToCollision = (m_WallsCollided[i].point - (Vector2)transform.position).normalized;
        float dotToCollision = Vector2.Dot( moveDir, vecToCollision );
        if( dotToCollision > minDotProductToCollision )
        {
          minDotProductToCollision = dotToCollision;
          targetIndex = i;
        }
      }
      
      if( m_WallsCollided[targetIndex].collider != m_formerCollider )
      {
        m_lastTimeSwitchedWalls = Time.time;
        m_formerCollider = m_WallsCollided[targetIndex].collider;
      }

      // Calculate where we touch the wall
      // Get a clean raycast directly through our feet to our target surface.
      // Otherwise, the earlier CircleCast intersects ahead or behind our center point
      // Do not do this if we have just switched surfaces and have yet to reorient our feet
      // Fall back to the collision point from the earlier CircleCast
      RaycastHit2D raycastToWall = m_WallsCollided[targetIndex];
      bool reachedEdgeOfSurface = false;

      if( Time.time - m_lastTimeSwitchedWalls > Mathf.Epsilon )
      {
        // ASSUMPTION: our facing is upright from surface, and is not lerping towards this
        //RaycastHit2D rayToFeet = Physics2D.Raycast( transform.position, -transform.up, m_touchingWallRadius, m_WallLayers );

        // NEW STRATEGY: out from the wall's normal
        RaycastHit2D rayToFeet = Physics2D.Raycast( transform.position, -m_WallsCollided[targetIndex].normal, m_touchingWallRadius, m_WallLayers );


        Debug.Log( "ray hit: distance = " + rayToFeet.distance + ", collider null? " + ( rayToFeet.collider == null ) +
          ", direction of ray = " + -transform.right );
        //Debug.Log( "old collider = " + m_WallsCollided[targetIndex].collider.gameObject.GetInstanceID() +
        //  ", new collider = " + rayToFeet.collider.gameObject.GetInstanceID() );

        if( rayToFeet.collider == m_WallsCollided[targetIndex].collider )
        {
          raycastToWall = rayToFeet;
          Debug.Log( "rayCast success! vector = " + ( ( -transform.right - transform.position ) * m_touchingWallRadius ) +
            ", impact point is " + rayToFeet.point );
        }
        
        // Raycast from the foot on the leading edge of the direction we're moving,
        // to check if we're moving over a slope
        // The ray is distance-limited to restrict maximum slope transition (i.e., 90 degrees fails, period, length determines max angle)
        bool movingRelativeLeft = Vector2.Dot( moveDir.normalized, transform.right) < 0f;
        Vector3 footWorldPos;
        if( movingRelativeLeft ) // cast from left foot
        {
          footWorldPos = transform.TransformPoint( m_leftFootRelativePos );
        }
        else // right foot
        {
          footWorldPos = transform.TransformPoint( new Vector3( -m_leftFootRelativePos.x, m_leftFootRelativePos.y, 0f ) );
        }
        RaycastHit2D footRay = Physics2D.Raycast( footWorldPos, -transform.up, m_maxSlopeAdjustDistance, m_WallLayers );

        if( footRay.collider == null )
        {
          reachedEdgeOfSurface = true;
          Debug.Log( "NO FOOT collider, footWorldPos = " + footWorldPos + ", footDir = " + -transform.right );
        }
        else
        {
          if( rayToFeet.collider == null )
          {
            raycastToWall = footRay;
            Debug.Log( "switch to FOOT" );
            Debug.Break();
          }
        }
      }

      Vector2 rayVector = (raycastToWall.point - (Vector2)transform.position).normalized;

      float dotToCollisionRay = Vector2.Dot( Quaternion.Euler(0f, 0f, -90f ) * moveDir.normalized, rayVector);
      float moveDirMagnitude = moveDir.magnitude;
      Vector2 wallRight = Quaternion.Euler( 0f, 0f, 90f ) * raycastToWall.normal;

      moveDirMagnitude = m_wallSlideInputScalar.Evaluate( dotToCollisionRay ) * moveDirMagnitude;

      moveDir = ( dotToCollisionRay > 0f ? 1f : -1f ) * -wallRight * moveDirMagnitude;
      //Debug.Log( "moveDir = " + moveDir + ", dotToCollisionRay = " + dotToCollisionRay );

      Vector2 accelerationForce = moveDir * m_AccelerationRateHuggingWall * Time.deltaTime;
      
      // Convert all current velocity to being along the plane we're touching
      Vector2 currentVelocity = m_Rigidbody2D.velocity;
      float dotOfCurrentToPlane = Vector2.Dot( currentVelocity, wallRight );
      Vector2 currentVelocityAlongPlane = wallRight *  dotOfCurrentToPlane;
      //Debug.Log( "currentVelocity = " + currentVelocity +
      //  ", dotOfCurrentToPlane = " + dotOfCurrentToPlane +
      //  ", curentVelocityAlongPlane = " + currentVelocityAlongPlane );

      outputVelocity = currentVelocityAlongPlane + ( accelerationForce * GetReverseDirectionAccelerationBoost( moveDir ) );

      if( reachedEdgeOfSurface )
      {
        outputVelocity = Vector2.zero;
        m_Rigidbody2D.velocity = Vector2.zero;
      }

      // CAP VELOCITY at max
      outputVelocityMagnitude = Mathf.Min( outputVelocity.magnitude, m_MaxVelocityHuggingWall );

      // SNAP POSITION to the wall
      Vector2 targetPosition = raycastToWall.point + ( -rayVector * m_wallHugOffset );
      Debug.Log( "set position to " + targetPosition + 
        ", collision point = " + raycastToWall.point + 
        ", curPos = " + transform.position +
        ", -rayVector * offset = " + (-rayVector * m_wallHugOffset ) );
      transform.position = targetPosition;

      // SET FACING
      // When on a wall, always face with feet towards wall
      // 
      if( moveDir.sqrMagnitude > Mathf.Epsilon )
      {
        // DEBUG
        Vector2 oldFacing = transform.up;

        transform.up = raycastToWall.normal;

        // DEBUG
        float facingChangeDot = Vector2.Dot( oldFacing, transform.up);
        if( facingChangeDot < 0.3 )
        {
          Debug.Log( "dot is " + facingChangeDot );
          //Debug.Break();
        }
      }
    }
    else // add force from input direction
    {
      Vector2 accelerationForce = moveDir * m_AccelerationRate * Time.deltaTime;

      outputVelocity = m_Rigidbody2D.velocity + ( accelerationForce * GetReverseDirectionAccelerationBoost( moveDir ) );

      // Cap at max velocity
      outputVelocityMagnitude = Mathf.Min( outputVelocity.magnitude, m_MaxVelocity );
      
      // SET FACING
      // At the moment, we're snapping it to velocity every frame, not doing an animated lerp
      // Change this if we have more robust character facing animation
      if( moveDir.sqrMagnitude > Mathf.Epsilon )
      {
        transform.up = new Vector3( moveDir.x, moveDir.y, 0f );
      }
    }


    // Apply friction; scale friction based on input magnitude (so it doesn't affect us at full throttle as much)
    float frictionScalar = m_frictionAtInputMagnitude.Evaluate( moveDir.magnitude );
    float friction = 1f - (m_Friction * frictionScalar * Time.deltaTime);
    outputVelocityMagnitude *= friction;

    outputVelocity = outputVelocity.normalized * outputVelocityMagnitude;

    //Debug.Log( //"moveDir = " + moveDir +
    //  //", accelerationForce = " + accelerationForce +
    //  //", reverseDirectionAccelerationBoost = " + reverseDirectionAccelerationBoost +
    //  //", angleDiff = " + angleDiff +
    //  ", outputVelocity = " + outputVelocity +
    //  ", magnitude is " + outputVelocity.magnitude +
    //  ", friction = " + friction
    //  );
    
    m_Rigidbody2D.velocity = outputVelocity;

    if( m_debug )
    {
      if( m_TouchingWall && hugWall )
      {
        DebugColorCollider( m_WallsCollided[targetIndex].collider, Color.magenta );
        if( m_WallsCollided[targetIndex].collider != m_DEBUG_formerCollider &&
            m_DEBUG_formerCollider != null )
        {
          DebugColorCollider( m_DEBUG_formerCollider, Color.white );
        }
      }
      else if( m_DEBUG_formerCollider != null )
      {
        DebugColorCollider( m_DEBUG_formerCollider, Color.white );
      }

      m_DEBUG_formerCollider = m_WallsCollided[targetIndex].collider;
    }
  }

  /// <summary> 
  /// Scale acceleration force based on angle difference from current velocity,
  /// so we change directions faster
  /// Use dot product for angle difference. We want them to be near-normalized
  /// so results are [-1, 1], but don't normalize as we don't want to inadvertantly
  /// add acceleration if we are holding input at half-strength
  /// </summary>
  /// <param name="moveDir"></param>
  /// <returns></returns>
  private float GetReverseDirectionAccelerationBoost( Vector2 moveDir )
  {
    float dot = Vector2.Dot( moveDir.normalized, m_Rigidbody2D.velocity.normalized );
    return m_reverseDirectionAngleScalar.Evaluate( dot );
  }

  private void DebugColorCollider( Collider2D colliderToColor, Color targetColor )
  {
    Renderer[] childRenderers = colliderToColor.transform.parent.GetComponentsInChildren<Renderer>();
    for( int i = 0; i < childRenderers.Length; ++i )
    {
      childRenderers[i].material.color = targetColor;
    }
  }

  ///// EDITOR

  private void OnDrawGizmosSelected()
  {
    // Draw the wall-check overlap sphere
    Gizmos.color = Color.magenta;
    Gizmos.DrawWireSphere( transform.position, m_touchingWallRadius );

    // Draw the left, right foot raycast position
    Gizmos.color = Color.green;
    Vector3 leftFootWorldPos = transform.TransformPoint( m_leftFootRelativePos );
    Gizmos.DrawWireSphere( leftFootWorldPos, 0.15f );
    Gizmos.DrawLine( leftFootWorldPos, transform.TransformPoint( new Vector3( m_leftFootRelativePos.x, m_leftFootRelativePos.y + -.45f, 0f ) ) );

    Vector3 rightFootWorldPos = transform.TransformPoint( new Vector3( -m_leftFootRelativePos.x, m_leftFootRelativePos.y, 0f ) );
    Gizmos.DrawWireSphere( rightFootWorldPos, 0.15f );
    Gizmos.DrawLine( rightFootWorldPos, transform.TransformPoint( new Vector3( -m_leftFootRelativePos.x, m_leftFootRelativePos.y + -.45f, 0f ) ) );
  }

}
