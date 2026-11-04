using System;
using System.Collections.Generic;
using UnityEngine;

// Basic melee only. Each target has its own capacity and start spacing. Ranged
// and special attacks never enter this pool. Explicit time makes expiry testable.
public sealed class EnemyMeleeAttackPool
{
    sealed class TargetBudget { public Transform Target; public float NextStart; }
    sealed class Entry
    {
        public EnemyCoordinationMember Member;
        public TargetBudget Budget;
        public long Ticket;
        public float LastRequest, Expires;
        public bool Ready, Granted, Committed;
    }
    readonly List<Entry> entries = new List<Entry>();
    readonly List<TargetBudget> budgets = new List<TargetBudget>();
    long nextTicket;
    const float RequestHeartbeat = 0.6f;
    public int PendingCount { get { int n = 0; foreach (var e in entries) if (!e.Granted) n++; return n; } }
    public int ActiveCount { get { int n = 0; foreach (var e in entries) if (e.Granted) n++; return n; } }
    public int CommittedCount { get { int n = 0; foreach (var e in entries) if (e.Committed) n++; return n; } }
    Entry Find(EnemyCoordinationMember member) { foreach (var e in entries) if (e.Member == member) return e; return null; }
    public bool HasPermission(EnemyCoordinationMember member) => Find(member)?.Granted == true;
    public bool IsCommitted(EnemyCoordinationMember member) => Find(member)?.Committed == true;
    public int CountForTarget(Transform target)
    { int count = 0; foreach (var e in entries) if (e.Granted && e.Budget.Target == target) count++; return count; }
    public bool Request(EnemyCoordinationMember member, Transform target, float now, bool ready)
    {
        var entry = Find(member);
        if (entry == null)
        {
            TargetBudget budget = null;
            foreach (var item in budgets) if (item.Target == target) { budget = item; break; }
            if (budget == null) { budget = new TargetBudget { Target = target }; budgets.Add(budget); }
            entry = new Entry { Member = member, Budget = budget, Ticket = nextTicket++ }; entries.Add(entry);
        }
        if (entry.Budget.Target != target) return false; // owner must release before switching targets
        entry.LastRequest = now; entry.Ready = ready;
        if (entry.Granted && !entry.Committed && !ready)
        { entry.Granted = false; entry.Ticket = nextTicket++; }
        return entry.Granted;
    }
    public void Commit(EnemyCoordinationMember member, float now, float maximumHold)
    {
        var entry = Find(member);
        if (entry == null || !entry.Granted || entry.Committed) return;
        entry.Committed = true; entry.Expires = now + maximumHold;
    }
    public void Release(EnemyCoordinationMember member, bool cancelCommitted, Action<EnemyCoordinationMember> cancel)
    {
        var entry = Find(member); if (entry == null) return;
        entries.Remove(entry);
        if (cancelCommitted && entry.Committed) cancel(member);
        if (member != null) member.DescribeAttack("No reservation");
        PruneBudgets();
    }
    public void CancelQueued(EnemyCoordinationMember member)
    {
        var entry = Find(member);
        if (entry == null || entry.Committed) return;
        entries.Remove(entry); PruneBudgets();
        if (member != null) member.DescribeAttack("Not requesting");
    }
    public void Clear(Action<EnemyCoordinationMember> cancel)
    {
        foreach (var e in entries)
        {
            if (e.Committed) cancel(e.Member);
            if (e.Member != null) e.Member.DescribeAttack("Coordination stopped");
        }
        entries.Clear(); budgets.Clear();
    }
    public void Tick(float now, int capacity, float startSpacing, float grantLease,
        Func<EnemyCoordinationMember, Transform, bool> valid, Func<EnemyCoordinationMember, bool> executing,
        Action<EnemyCoordinationMember> cancel)
    {
        for (int i = entries.Count - 1; i >= 0; i--)
        {
            var entry = entries[i];
            bool unavailable = !valid(entry.Member, entry.Budget.Target);
            bool expired = entry.Committed && now >= entry.Expires;
            bool complete = entry.Committed && !executing(entry.Member);
            bool abandoned = !entry.Committed && now - entry.LastRequest > RequestHeartbeat;
            if (unavailable || expired || complete || abandoned)
            {
                entries.RemoveAt(i);
                if (entry.Committed && (unavailable || expired)) cancel(entry.Member);
                if (entry.Member != null) entry.Member.DescribeAttack(expired ? "Reservation timed out" : "No reservation");
                continue;
            }
            if (entry.Granted && !entry.Committed && now >= entry.Expires)
            { entry.Granted = false; entry.Ticket = nextTicket++; entry.Member.DescribeAttack("Grant expired; queued again"); }
        }
        PruneBudgets();
        // Grant oldest READY request, not first actor in Unity's Update order.
        // An obstructed/retreating older requester cannot block the whole group.
        while (true)
        {
            Entry oldest = null;
            foreach (var entry in entries)
            {
                if (entry.Granted || !entry.Ready || now < entry.Budget.NextStart || CountForTarget(entry.Budget.Target) >= capacity) continue;
                if (oldest == null || entry.Ticket < oldest.Ticket) oldest = entry;
            }
            if (oldest == null) break;
            oldest.Granted = true; oldest.Expires = now + grantLease;
            oldest.Budget.NextStart = now + startSpacing;
            oldest.Member.DescribeAttack("Granted; awaiting attack start");
        }
    }
    void PruneBudgets()
    {
        for (int i = budgets.Count - 1; i >= 0; i--)
        {
            bool used = false;
            foreach (var entry in entries) if (entry.Budget == budgets[i]) { used = true; break; }
            if (!used) budgets.RemoveAt(i);
        }
    }
}
