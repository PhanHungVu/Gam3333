using System;
using Unity.Behavior;
using Unity.Properties;
using UnityEngine;
using Action = Unity.Behavior.Action;

namespace Lithomachia.EnemyBehavior
{
    [Serializable]
    public abstract class EnemyGraphAction : Action
    {
        [NonSerialized] protected EnemyGraphController Controller;
        protected sealed override Status OnStart()
        {
            Controller = GameObject.GetComponent<EnemyGraphController>();
            if (Controller == null || !Controller.IsReady) return Status.Failure;
            Begin(); return Step();
        }
        protected sealed override Status OnUpdate() => Step();
        Status Step()
        {
            if (Controller == null || !Controller.IsReady) return Status.Failure;
            return Time.deltaTime <= 0 ? Status.Running : Tick();
        }
        protected virtual void Begin() { }
        protected abstract Status Tick();
    }

    [Serializable, GeneratePropertyBag]
    [NodeDescription(name:"Detect Player", story:"Detect player", category:"Lithomachia/Enemy", id:"822bf5976a9c49efb19522b3ca0668c0")]
    public partial class DetectEnemyPlayerAction : EnemyGraphAction
    {
        protected override Status Tick() => Controller.SensePlayer() ? Status.Success : Status.Failure;
    }

    [Serializable, GeneratePropertyBag]
    [NodeDescription(name:"Wander Until Player Detected", story:"Wander until player detected", category:"Lithomachia/Enemy", id:"42ba3aeae12b425da949ccda1961a38f9")]
    public partial class WanderEnemyAction : EnemyGraphAction
    {
        protected override void Begin() => Controller.Agent.Wander.Enter();
        protected override Status Tick()
        {
            if (Controller.SensePlayer()) { Controller.Hold("Detected player"); return Status.Success; }
            Controller.Wander(); return Status.Running;
        }
    }

    [Serializable, GeneratePropertyBag]
    [NodeDescription(name:"Approach Attack Range", story:"Approach attack range", category:"Lithomachia/Enemy", id:"f104b8619db84315999a6013ed279084")]
    public partial class ApproachEnemyAction : EnemyGraphAction
    {
        protected override void Begin() => Controller.BeginApproach();
        protected override Status Tick()
        {
            if (!Controller.SensePlayer()) { Controller.Hold("Lost player"); return Status.Failure; }
            return Controller.Approach() ? Status.Success : Status.Running;
        }
    }

    [Serializable, GeneratePropertyBag]
    [NodeDescription(name:"Request Weapon Attack", story:"Request weapon attack", category:"Lithomachia/Enemy", id:"1fbd2471ec2a41b88bad9cc8e502427e")]
    public partial class RequestEnemyAttackAction : EnemyGraphAction
    {
        protected override Status Tick()
        {
            Controller.Hold("Request attack");
            if (!Controller.SensePlayer()) return Status.Failure;
            if (Controller.ContinuousMeleePressure || Controller.UsesRangedPositioning)
            {
                bool inRange = Controller.Approach();
                if (!inRange || Controller.CooldownRemaining > 0) return Status.Running;
                return Controller.RequestAttack() ? Status.Success : Status.Running;
            }
            if (Controller.UsesMeleeAttackCoordination)
            {
                Controller.Approach();
                if (Controller.CooldownRemaining > 0) Controller.CancelQueuedAttackRequest();
                else if (Controller.RequestAttack()) return Status.Success;
                Controller.RepositionWhileWaitingForAttack(); return Status.Running;
            }
            if (!Controller.TargetInReach) return Status.Failure;
            if (Controller.CooldownRemaining > 0) return Status.Running;
            return Controller.RequestAttack() ? Status.Success : Status.Failure;
        }
        protected override void OnEnd() { if (Controller != null) Controller.CancelQueuedAttackRequest(); }
    }

    [Serializable, GeneratePropertyBag]
    [NodeDescription(name:"Wait For Weapon Attack", story:"Wait for weapon attack", category:"Lithomachia/Enemy", id:"48e8f62d4ecb4d8d8b3334a212a34449")]
    public partial class WaitEnemyAttackAction : EnemyGraphAction
    {
        protected override Status Tick()
        {
            Controller.Hold("Attack windup / release");
            if (!Controller.SensePlayer()) { Controller.CancelAttack(); return Status.Failure; }
            return Controller.AttackInProgress ? Status.Running : Status.Success;
        }
        protected override void OnEnd() { if (Controller != null) Controller.CancelAttack(); }
    }

    [Serializable, GeneratePropertyBag]
    [NodeDescription(name:"Recover Weapon Cooldown", story:"Recover weapon cooldown", category:"Lithomachia/Enemy", id:"a8110da821b546f0a5d10af059770ce7")]
    public partial class RecoverEnemyAction : EnemyGraphAction
    {
        [NonSerialized] bool completed;
        protected override void Begin() => completed = false;
        protected override Status Tick()
        {
            Controller.Hold("Recover");
            if (!Controller.SensePlayer()) return Status.Failure;
            completed = Controller.Recover(); return completed ? Status.Success : Status.Running;
        }
        protected override void OnEnd() { if (!completed && Controller != null) Controller.CancelAttack(); }
    }
}
