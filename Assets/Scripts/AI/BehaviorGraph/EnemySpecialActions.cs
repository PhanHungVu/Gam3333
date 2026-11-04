using System;
using Unity.Behavior;
using Unity.Properties;

namespace Lithomachia.EnemyBehavior
{
    [Serializable, GeneratePropertyBag]
    [NodeDescription(name:"Telegraph Special Attack", story:"Telegraph special attack", category:"Lithomachia/Enemy", id:"a29391ed7f7a4e9a887281895f980763")]
    public partial class WindupEnemySpecialAction : EnemyGraphAction
    {
        [NonSerialized] bool began, completed;
        protected override void Begin() { began = completed = false; }
        protected override Status Tick()
        {
            Controller.Hold("Special windup");
            if (Controller.SpecialAttack == null || !Controller.SensePlayer()) return Status.Failure;
            if (!began)
            {
                if (Controller.UsesSpecialCoordination)
                {
                    if (!Controller.TryBeginSpecial()) { Controller.RepositionSpecial(); return Status.Running; }
                    began = true;
                }
                else
                {
                    if (Controller.CooldownRemaining > 0) return Status.Running;
                    if (!Controller.SpecialAttack.BeginWindup()) return Status.Failure; began = true;
                }
            }
            completed = Controller.SpecialAttack.TickWindup(UnityEngine.Time.deltaTime);
            return completed ? Status.Success : Status.Running;
        }
        protected override void OnEnd() { if (!completed && Controller != null) Controller.CancelAttack(); }
    }
    [Serializable, GeneratePropertyBag]
    [NodeDescription(name:"Execute Special Attack", story:"Execute special attack", category:"Lithomachia/Enemy", id:"996b63a701b847b3a91b7c2a95bbc0ea")]
    public partial class ExecuteEnemySpecialAction : EnemyGraphAction
    {
        [NonSerialized] bool completed;
        protected override void Begin() { completed = false; if (Controller.SpecialAttack != null) Controller.SpecialAttack.BeginAttack(); }
        protected override Status Tick()
        {
            if (Controller.SpecialAttack == null || !Controller.SensePlayer()) { Controller.CancelAttack(); return Status.Failure; }
            Controller.Hold(Controller.SpecialAttack.CurrentStage switch
            {
                EnemyGraphAttack.Stage.Grounded => "Rock resting on ground",
                EnemyGraphAttack.Stage.OrbitLift => "Lifting rock",
                EnemyGraphAttack.Stage.Orbit => "Orbiting rock",
                _ => "Special attack"
            });
            completed = Controller.SpecialAttack.TickAttack(UnityEngine.Time.deltaTime); return completed ? Status.Success : Status.Running;
        }
        protected override void OnEnd() { if (!completed && Controller != null) Controller.CancelAttack(); }
    }
    [Serializable, GeneratePropertyBag]
    [NodeDescription(name:"Pull Toward Landed Rock", story:"Pull toward landed rock", category:"Lithomachia/Enemy", id:"d6ef018cf89946dabf3d47649d192c13")]
    public partial class PullEnemyRockAction : EnemyGraphAction
    {
        [NonSerialized] bool completed;
        protected override void Begin() { completed = false; if (Controller.SpecialAttack != null) Controller.SpecialAttack.BeginPull(); }
        protected override Status Tick()
        {
            Controller.Hold("Pull toward landed rock");
            if (Controller.SpecialAttack == null || !Controller.SensePlayer()) { Controller.CancelAttack(); return Status.Failure; }
            completed = Controller.SpecialAttack.TickPull(UnityEngine.Time.deltaTime); return completed ? Status.Success : Status.Running;
        }
        protected override void OnEnd() { if (!completed && Controller != null) Controller.CancelAttack(); }
    }
}
