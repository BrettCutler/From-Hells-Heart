using UnityEngine;
using System.Collections;

// Right now, only one player character controller
[RequireComponent( typeof( PlayerCharacterController ) )]
public class PlayerMoveControl : MonoBehaviour
{
  private PlayerCharacterController m_CharacterController;
  private bool m_JumpedThisUpdate;
  private bool m_hugWall;

  private void Awake()
  {
    // Get referenes, the SLOW way
    m_CharacterController = GetComponent<PlayerCharacterController>();
  }

  // NOTE that the example MoveControl script reads button presses in Update, saves their state, and
  // passes it in during FixedUpdate.
  // This appears to be in case Update hits 2+ times between FixedUpdate, to bias towards confirming
  // a jump.
  private void Update()
  {
    if( !m_JumpedThisUpdate )
    {
      m_JumpedThisUpdate = Input.GetButtonDown( "Jump" );
    }
  }

  private void FixedUpdate()
  {
    Vector2 moveDir = new Vector2( Input.GetAxis("Horizontal"), Input.GetAxis("Vertical"));
    
    // If on keyboard, holding diagonal will create a vector of magnitude > 1, so clamp
    moveDir = Vector2.ClampMagnitude( moveDir, 1f );

    float hugWallRaw = Input.GetAxis( "HugWall" );
    m_hugWall = hugWallRaw > 0f;
    
    m_CharacterController.Move( moveDir, m_hugWall, m_JumpedThisUpdate );
    m_JumpedThisUpdate = false;
  }
}
