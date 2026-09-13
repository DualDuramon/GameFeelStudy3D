using System;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.EventSystems;


using static MeshSlicerAction;

[CreateAssetMenu(fileName = "PlayerMeshSlicerInput", menuName = "Scriptable Objects/PlayerMeshSlicerInput")]
public class PlayerMeshSlicerInput : ScriptableObject, IMeshSlicerActions
{
    [SerializeField] private MeshSlicerAction _inputActions;
    [SerializeField] private bool _isMouseClickInputLocked = true;
    public bool IsMouseClickLocked => _isMouseClickInputLocked;


    public event Action OnNormalAttackEvent;
    public event Action OnMouseClickEvent;
    public event Action OnEscKeyEvent;


    public void OnEnable()
    {
        if (_inputActions == null)
        {
            _inputActions = new MeshSlicerAction();
            _inputActions.MeshSlicer.SetCallbacks(this);
        }
        _inputActions.Enable();

        if (Application.isPlaying)
        {
            SetMouseInputLocked(_isMouseClickInputLocked);
        }
    }

    public void OnDisable()
    {
        _inputActions.Disable();

        if (Application.isPlaying)
        {
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;
        }
    }

    public void OnNormalAttack(InputAction.CallbackContext context)
    {
        if (!context.performed) return;

        if (_isMouseClickInputLocked)
        {
            if (EventSystem.current != null && EventSystem.current.IsPointerOverGameObject())
            {
                return;
            }

            SetMouseInputLocked(false);
            return;
        }

        OnNormalAttackEvent?.Invoke();
    }

    private void SetMouseInputLocked(bool locked)
    {
        _isMouseClickInputLocked = locked;

        // UI mode leaves the pointer free and visible; targeting mode locks it
        // to the Game view, which also guarantees that it is hidden.
        Cursor.lockState = locked ? CursorLockMode.None : CursorLockMode.Locked;
        Cursor.visible = locked;
    }

    public void OnEscInput(InputAction.CallbackContext context)
    {
        if(context.performed)
        {
            SetMouseInputLocked(true);
            OnEscKeyEvent?.Invoke();
        }
    }
}
