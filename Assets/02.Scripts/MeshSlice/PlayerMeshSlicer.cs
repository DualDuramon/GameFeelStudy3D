using UnityEngine;
using RuntimeMeshSlicing;

public class PlayerMeshSlicer : MonoBehaviour
{
    [SerializeField] private PlayerMeshSlicerInput _input;
    [SerializeField] private Animator _animator;
    [SerializeField] private AnimatedBladeSlicer[] _bladesSlicer;

    [SerializeField] private const string Attaci_Trigger_Name = "AttackTrigger";
    private bool _canAttack = true;


    private void Awake()
    {
        _animator = GetComponent<Animator>();
        _bladesSlicer = GetComponentsInChildren<AnimatedBladeSlicer>();
    }

    private void OnEnable()
    {
        _input.OnNormalAttackEvent += HandleNormalAttack;
    }

    private void OnDisable()
    {
        _input.OnNormalAttackEvent -= HandleNormalAttack;
    }

    private void HandleNormalAttack()
    {
        if(_canAttack)
        {
            _animator.SetTrigger(Attaci_Trigger_Name);
        }
    }

    // Animation Event에서 호출할 함수들
    public void BeginBladeWindow()
    {
        foreach (AnimatedBladeSlicer slicer in _bladesSlicer)
        {
            slicer.BeginSliceWindow();
        }
    }
    public void EndBladeWindow()
    {
        foreach (AnimatedBladeSlicer slicer in _bladesSlicer)
        {
            slicer.EndSliceWindow();
        }
    }

    public void ToggleAttackFlag()
    {
        _canAttack = !_canAttack;
    }
}
