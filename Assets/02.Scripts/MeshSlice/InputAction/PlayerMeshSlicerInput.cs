using System;
using UnityEngine;
using UnityEngine.InputSystem;

using static MeshSlicerAction;

[CreateAssetMenu(fileName = "PlayerMeshSlicerInput", menuName = "Scriptable Objects/PlayerMeshSlicerInput")]
public class PlayerMeshSlicerInput : ScriptableObject, IMeshSlicerActions
{
    [SerializeField] private MeshSlicerAction _inputActions;
    public event Action OnNormalAttackEvent;


    public void OnEnable()
    {
        if (_inputActions == null)
        {
            _inputActions = new MeshSlicerAction();
            _inputActions.MeshSlicer.SetCallbacks(this);
        }
        _inputActions.Enable();
    }

    public void OnDisable()
    {
        _inputActions.Disable();
    }

    public void OnNormalAttack(InputAction.CallbackContext context)
    {
        if(context.performed)
        {
            OnNormalAttackEvent?.Invoke();
        }
    }
}
