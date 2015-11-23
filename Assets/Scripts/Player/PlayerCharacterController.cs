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
  [SerializeField] private float m_wallHugOffset; // How close to a wall do we snap when hugging?
  [SerializeField] private float m_touchingWallRadius; // Size of collider to touch walls
  [SerializeField] private bool m_debug; // Enable debug logging, drawing?

  private bool m_TouchingWall;              // Whether or not player is touching environment. Updated every frame
  private List<Collider2D> m_WallsTouching = new List<Collider2D>(); // Container for walls player is touching. Updated every frame
  private Rigidbody2D m_Rigidbody2D;
  private RaycastHit2D[] m_WallsCollided = new RaycastHit2D[k_maxNumberOfWallCollisions];
  private float m_lastTimeSwitchedWalls;

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
  public void Move( Vector2 moveDir, bool hugWall )
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

        Vector2 vecToCollision = (m_WallsCollided[i].point - (Vector2)transform.position).normalized;
        float dotToCollision = Vector2.Dot( transform.up, vecToCollision );
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

      Vector2 rayVector = (m_WallsCollided[targetIndex].point - (Vector2)transform.position).normalized;
      float dotToCollisionRay = Vector2.Dot( Quaternion.Euler(0f, 0f, -90f ) * moveDir.normalized, rayVector);
      float moveDirMagnitude = moveDir.magnitude;
      Vector2 wallRight = Quaternion.Euler( 0f, 0f, 90f ) * m_WallsCollided[targetIndex].normal;

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

      // Cap at max velocity
      outputVelocityMagnitude = Mathf.Min( outputVelocity.magnitude, m_MaxVelocityHuggingWall );

      // Snap position to the wall
      transform.position = m_WallsCollided[targetIndex].point + ( -rayVector * m_wallHugOffset );
    }
    else // add force from input direction
    {
      Vector2 accelerationForce = moveDir * m_AccelerationRate * Time.deltaTime;

      outputVelocity = m_Rigidbody2D.velocity + ( accelerationForce * GetReverseDirectionAccelerationBoost( moveDir ) );

      // Cap at max velocity
      outputVelocityMagnitude = Mathf.Min( outputVelocity.magnitude, m_MaxVelocity );
    }


    // Apply friction; scale friction based on input magnitude (so it doesn't affect us at full throttle as much)
    float frictionScalar = m_frictionAtInputMagnitude.Evaluate( moveDir.magnitude );
    float friction = 1f - (m_Friction * frictionScalar * Time.deltaTime);
    outputVelocityMagnitude *= friction;

    outputVelocity = outputVelocity.normalized * outputVelocityMagnitude;

    //Debug.Log( //"moveDir = " + moveDir +
    //  ", accelerationForce = " + accelerationForce +
    //  ", reverseDirectionAccelerationBoost = " + reverseDirectionAccelerationBoost +
    //  ", angleDiff = " + angleDiff +
    //  ", outputVelocity = " + outputVelocity +
    //  ", magnitude is " + outputVelocity.magnitude +
    //  ", friction = " + friction
    //  );

    m_Rigidbody2D.velocity = outputVelocity;

    if( m_Rigidbody2D.velocity.sqrMagnitude > Mathf.Epsilon )
    {
      transform.up = new Vector3( m_Rigidbody2D.velocity.x, m_Rigidbody2D.velocity.y, 0f );
    }

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
  }

}
