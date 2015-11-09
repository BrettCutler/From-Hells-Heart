using UnityEngine;
using System.Collections;
using System.Collections.Generic;

public class PlayerCharacterController : MonoBehaviour
{
  [SerializeField] private float m_MaxVelocity;
  [SerializeField] private float m_AccelerationRate;
  [SerializeField] [Range(0, 10)] private float m_Friction;
  [SerializeField] private LayerMask m_WallLayers;  // Layermask determining what is a wall to the character
  // Varying friction based on input magnitude allows us to slow down faster when letting go
  [SerializeField] private AnimationCurve m_frictionAtInputMagnitude; 

  private bool m_TouchingWall;              // Whether or not player is touching environment. Updated every frame
  private List<Collider2D> m_WallsTouching = new List<Collider2D>(); // Container for walls player is touching. Updated every frame
  private Rigidbody2D m_Rigidbody2D;

  const float k_touchingWallRadius = 5f;

  private void Awake()
  {
    // Setting up references -- the SLOW way
    m_Rigidbody2D = GetComponent<Rigidbody2D>();
  }

  private void FixedUpdate()
  {
    m_TouchingWall = false;

    // The player is touching the wall if a circlecast
    // at their posiion hits anything designated as ground
    m_WallsTouching.Clear();
    Collider2D[] colliders = Physics2D.OverlapCircleAll( transform.position, k_touchingWallRadius, m_WallLayers );
    for( int i = 0; i < colliders.Length; ++i )
    {
      if( colliders[i].gameObject != gameObject )
      {
        m_WallsTouching.Add( colliders[i] );
        m_TouchingWall = true;
      }
    }

    // If player is touching multiple walls, select the one the analog stick is pointing towards
    //// TODO

    // If we're touching a wall, 
  }

  /// <summary>
  /// Move is called by an attached [*]Control script,
  /// to allow the same character controller to be used by AIs and Players
  /// </summary>
  /// <param name="moveDir">Direction + magnitude</param>
  public void Move( Vector2 moveDir, bool jump )
  {
    // If touching wall, slide along wall's edge
    if( m_TouchingWall )
    {
      //// TODO
    }
    else // move in direction indicated
    {
      // We'll handle acceleration/max velocity ourselves,
      // so we'll just set rigidbody velocity at the end
      //float curVelocityMagnitude = m_Rigidbody2D.velocity.magnitude;
      Vector2 outputVelocity = Vector2.zero;
      float outputVelocityMagnitude = 0f;

      //Vector2 desiredVelocity = moveDir * m_MaxVelocity;
      //Vector2 velocityDifference = m_Rigidbody2D.velocity - desiredVelocity;

      Vector2 accelerationForce = moveDir * m_AccelerationRate * Time.deltaTime;
      
      outputVelocity = (m_Rigidbody2D.velocity + accelerationForce);

      // Limit at max velocity, apply friction
      outputVelocityMagnitude = Mathf.Min( outputVelocity.magnitude, m_MaxVelocity );

      float frictionScalar = m_frictionAtInputMagnitude.Evaluate( moveDir.magnitude );

      float friction = 1f - (m_Friction * frictionScalar * Time.deltaTime);
      outputVelocityMagnitude *= friction;

      outputVelocity = outputVelocity.normalized * outputVelocityMagnitude;

      //Debug.Log( //"moveDir = " + moveDir +
      //  ", accelerationForce = " + accelerationForce +
      //  ", outputVelocity = " + outputVelocity +
      //  ", magnitude is " + outputVelocity.magnitude +  
      //  ", friction = " + friction 
      //  );

      m_Rigidbody2D.velocity = outputVelocity;
    }
  }
}
