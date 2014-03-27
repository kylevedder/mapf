﻿using System;
using System.Linq;
using System.Collections.Generic;
using System.Diagnostics;

namespace CPF_experiment
{
    class CbsNode : IComparable<IBinaryHeapItem>, IBinaryHeapItem
    {
        public ushort totalCost;
        public ushort externalConflictsCount;
        public ushort internalConflictsCount;
        public SinglePlan[] allSingleAgentPlans;
        private int binaryHeapIndex;
        CbsConflict conflict;
        CbsConstraint constraint;
        CbsConstraint unConstraint;
        CbsNode prev;
        public ushort depth;
        public ushort[] agentsGroupAssignment;
        public static HashSet<CbsConstraint> allConstraintsForNode;
        public ushort replanSize;
        public byte collapse; // 0 - not expanded. 1 - only A not expanded. 2 - only B not expanded

        /// <summary>
        /// default constructor
        /// </summary>
        public CbsNode(int numberOfAgents)
        {
            allSingleAgentPlans = new SinglePlan[numberOfAgents];
            totalCost = 0;
            depth = 0;
            replanSize = 1;
            externalConflictsCount = 0;
            internalConflictsCount = 0;
            collapse = 0;
            agentsGroupAssignment = new ushort[numberOfAgents];
            for (ushort i = 0; i < numberOfAgents; i++)
            {
                agentsGroupAssignment[i] = i;
            }
        }
        /// <summary>
        /// child constructor
        /// </summary>
        /// <param name="father"></param>
        public CbsNode(CbsNode father, CbsConstraint newConstraint,int agentToReplan, ProblemInstance problem)
        {
            this.totalCost = father.totalCost;
            this.allSingleAgentPlans = new SinglePlan[father.allSingleAgentPlans.Length];
            for (int i = 0; i < this.allSingleAgentPlans.Length; i++)
            {
                this.allSingleAgentPlans[i] = father.allSingleAgentPlans[i];
            }
            this.agentsGroupAssignment = new ushort[father.agentsGroupAssignment.Length];
            for (int i = 0; i < agentsGroupAssignment.Length; i++)
            {
                this.agentsGroupAssignment[i] = father.agentsGroupAssignment[i];
            }
            this.prev = father;
            this.constraint = newConstraint;
            List<byte> group = new List<byte>();
            for (byte i = 0; i < agentsGroupAssignment.Length; i++)
            {
                if (i != agentToReplan && agentsGroupAssignment[i] == agentsGroupAssignment[agentToReplan])
                    group.Add(i);
            }
            this.constraint.addAgents(group);
            this.depth = (ushort)(prev.depth + 1);
            externalConflictsCount = 0;
            internalConflictsCount = 0;
            collapse = 0;
            replanSize = 1;
        }

        /// <summary>
        /// solve a given problem according to given constraints, sets the plans array (plan per agent)
        /// </summary>
        /// <param name="problem"></param>
        /// <param name="runner"></param>
        /// <returns></returns>
        public bool solve(ProblemInstance problem, Run runner, int depthToReplan, ICbsSolver solver, ref int highLevelExpanded, ref int highLevelGenerated, ref int loweLevelExpanded, ref int loweLevelGenerated)
        {
            totalCost = 0;
            HashSet<TimedMove> newInternalCAT = (HashSet<TimedMove>)problem.parameters[CBS_LocalConflicts.NEW_INTERNAL_CAT];
            HashSet<CbsConstraint> newConstraints = (HashSet<CbsConstraint>)problem.parameters[CBS_LocalConflicts.NEW_CONSTRAINTS];
            HashSet_U<TimedMove> InternalCAT = (HashSet_U<TimedMove>)problem.parameters[CBS_LocalConflicts.INTERNAL_CAT];
            HashSet_U<CbsConstraint> Constraints = (HashSet_U<CbsConstraint>)problem.parameters[CBS_LocalConflicts.CONSTRAINTS];

            newInternalCAT.Clear();
            newConstraints.Clear();
            ProblemInstance subProblem;

            for (int i = 0; i < problem.m_vAgents.Length; i++)
            {
                AgentState[] subGroup = new AgentState[] { problem.m_vAgents[i] };
                subProblem = problem.Subproblem(subGroup);
                subProblem.parameters = problem.parameters; // Why isn't this done in Subproblem()? Perhaps there's a good reason.

                InternalCAT.Join(newInternalCAT);
                Constraints.Join(newConstraints);

                solver.Setup(subProblem, depthToReplan);
                bool success = solver.Solve(runner);
                
                highLevelExpanded += solver.getHighLevelExpanded();
                highLevelGenerated += solver.getHighLevelGenerated();
                loweLevelExpanded += solver.getLowLevelExpanded();
                loweLevelGenerated += solver.getLowLevelGenerated();
                if (!success)
                {

                    InternalCAT.Seperate(newInternalCAT);
                    Constraints.Seperate(newConstraints);
                    return false;
                }

                InternalCAT.Seperate(newInternalCAT);
                Constraints.Seperate(newConstraints);

                allSingleAgentPlans[i] = solver.getSinglePlans()[0];
                totalCost += (ushort)solver.GetSolutionCost();
                allSingleAgentPlans[i].addPlanToHashSet(newInternalCAT, totalCost * 2);
            }
            this.FindConflict(problem);
            return true;
        }
        /// <summary>
        /// replan for a given agent (when constraints for that agent have changed)
        /// </summary>
        public bool rePlan(ProblemInstance problem, Run runner, int agentForRePlan, int depthToReplan, ICbsSolver highLevelSolver, ICbsSolver lowLevelSolver, ref int highLevelExpanded, ref int highLevelGenerated, ref int loweLevelExpanded, ref int loweLevelGenerated)
        {
            HashSet<TimedMove> newInternalCAT = (HashSet<TimedMove>)problem.parameters[CBS_LocalConflicts.NEW_INTERNAL_CAT];
            HashSet<CbsConstraint> newConstraints = (HashSet<CbsConstraint>)problem.parameters[CBS_LocalConflicts.NEW_CONSTRAINTS];
            HashSet_U<TimedMove> InternalCAT = (HashSet_U<TimedMove>)problem.parameters[CBS_LocalConflicts.INTERNAL_CAT];
            HashSet_U<CbsConstraint> Constraints = (HashSet_U<CbsConstraint>)problem.parameters[CBS_LocalConflicts.CONSTRAINTS];
            ICbsSolver solver = highLevelSolver;

            //this.setInvalid(newConstraints, this.agentsGroupAssignment[agentForRePlan]);
            this.setInvalid(newConstraints);
            newInternalCAT.Clear();

            int groupNum = this.agentsGroupAssignment[agentForRePlan];
            List<AgentState> subGroup=new List<AgentState>();

            //Debug.WriteLine("Sub-problem:");

            for (int i = 0; i < agentsGroupAssignment.Length; i++)
            {
                if (this.agentsGroupAssignment[i] == groupNum)
                {
                    subGroup.Add(problem.m_vAgents[i]);
                   // Debug.WriteLine(i);
                }
                else
                    allSingleAgentPlans[i].addPlanToHashSet(newInternalCAT, totalCost);
            }

            replanSize = (ushort)subGroup.Count;

            if (subGroup.Count == 1)
                solver = lowLevelSolver;

            ProblemInstance subProblem = problem.Subproblem(subGroup.ToArray());
            subProblem.parameters = problem.parameters;

            InternalCAT.Join(newInternalCAT);
            Constraints.Join(newConstraints);

            //Constraints.print();
           
            solver.Setup(subProblem, depthToReplan);
            if (solver.Solve(runner) == false)
            {
                highLevelExpanded += solver.getHighLevelExpanded();
                highLevelGenerated += solver.getHighLevelGenerated();
                loweLevelExpanded += solver.getLowLevelExpanded();
                loweLevelGenerated += solver.getLowLevelGenerated();
                InternalCAT.Seperate(newInternalCAT);
                Constraints.Seperate(newConstraints);
                return false;
            }
            highLevelExpanded += solver.getHighLevelExpanded();
            highLevelGenerated += solver.getHighLevelGenerated();
            loweLevelExpanded += solver.getLowLevelExpanded();
            loweLevelGenerated += solver.getLowLevelGenerated();



            int j = 0;
            SinglePlan[] singlePlans=solver.getSinglePlans();

            for (int i = 0; i < agentsGroupAssignment.Length; i++)
            {
                if (this.agentsGroupAssignment[i] == groupNum)
                {
                    this.allSingleAgentPlans[i] = singlePlans[j];
                    j++;
                }

            }

           // printPlan();

            totalCost = 0;
            foreach (SinglePlan plan in allSingleAgentPlans)
            {
                totalCost += (ushort)(plan.GetSize() - 1);
            }
            InternalCAT.Seperate(newInternalCAT);
            Constraints.Seperate(newConstraints);
            newConstraints.Clear();
            this.FindConflict(problem);
           // printConflict();
            return true;
        }
        /// <summary>
        /// find the first conflict (timewise) for all the given plans
        /// </summary>
        /// <returns></returns>
        private void FindConflict(ProblemInstance problem)
        {
            if (this.allSingleAgentPlans.Length == 1) 
                return;
            int maxPlanSize = 0;
            int planSize = -1;
            this.conflict = null;
            HashSet<TimedMove> externalCAT = null;
            HashSet_U<TimedMove> CbsExternalCAT = (HashSet_U<TimedMove>)problem.parameters[CBS_LocalConflicts.INTERNAL_CAT];
            if (problem.parameters.ContainsKey(Trevor.CONFLICT_AVOIDENCE))
                 externalCAT = (HashSet<TimedMove>)problem.parameters[Trevor.CONFLICT_AVOIDENCE];
            TimedMove checkMove=new TimedMove();

            // Find the longest plan among all the groups
            foreach (SinglePlan plan in this.allSingleAgentPlans)
            {
                planSize = plan.GetSize();
                if (planSize > maxPlanSize)
                    maxPlanSize = planSize;
            }

            // Check in every time step that the plans do not collide
            for (int time = 1; time < maxPlanSize; time++)
            {
                // Check all pairs of groups if they are conflicting at the given time step
                for (int i = 0; i < allSingleAgentPlans.Length; i++)
                {
                    checkMove.setup(allSingleAgentPlans[i].GetLocationsAt(time),time);
                    if (checkMove.isColliding(externalCAT))
                        externalConflictsCount++;
                    if (checkMove.isColliding(CbsExternalCAT))
                        externalConflictsCount++;
                    for (int j = i + 1; j < allSingleAgentPlans.Length; j++)
                    {
                        if (allSingleAgentPlans[i].IsColliding(time, allSingleAgentPlans[j]))
                        {
                            if (conflict == null)
                            {
                                Move first = allSingleAgentPlans[i].GetLocationsAt(time);
                                Move second = allSingleAgentPlans[j].GetLocationsAt(time);
                                this.conflict = new CbsConflict(i, j, first, second, time);
                            }
                            internalConflictsCount++;
                        }
                    }
                }
            }
        }

        public CbsConflict getConflict()
        {
            return this.conflict;
        }

        public override int GetHashCode()//TODO: change this
        {
            unchecked
            {
                int ans = 0;
                for (int i = 0; i < agentsGroupAssignment.Length; i++)
                {
                    ans += Constants.PRIMES_FOR_HASHING[i % Constants.PRIMES_FOR_HASHING.Length] * agentsGroupAssignment[i];
                }

                CbsNode current = this;
                while (current.depth > 0)
                {
                    if (current.prev.conflict != null && this.agentsGroupAssignment[current.prev.conflict.agentA] != this.agentsGroupAssignment[current.prev.conflict.agentB])
                    {
                        ans += current.constraint.GetHashCode();
                    }
                    current = current.prev;
                }
                return ans;
            }
        }

        private void normalizeGroups()
        {
            short[] newGroups = new short[agentsGroupAssignment.Length];
            short groupCount = 0;

            for (int i = 0; i < newGroups.Length; i++)
            {
                newGroups[i] = -1;
            }

            for (int i = 0; i < agentsGroupAssignment.Length; i++)
            {
                if (newGroups[agentsGroupAssignment[i]] == -1)
                {
                    newGroups[agentsGroupAssignment[i]] = groupCount;
                    groupCount++;
                }
            }
            for (int i = 0; i < agentsGroupAssignment.Length; i++)
            {
                agentsGroupAssignment[i] = (ushort)newGroups[agentsGroupAssignment[i]];
            }
        }

        public override bool Equals(object obj) 
        {
            CbsNode other = (CbsNode)obj;
            CbsNode current = this;
            //ushort[] OtherAgentsGroupAssignment = null;
            //if (CBS_LocalConflicts.isGlobal == false)
            //{
            //    for (int i = 0; i < agentsGroupAssignment.Length; i++)
            //    {
            //        if (other.agentsGroupAssignment[i] != this.agentsGroupAssignment[i])
            //            return false;
            //    }
            //}
            //else
            //{
            //    OtherAgentsGroupAssignment = other.agentsGroupAssignment;
            //    other.agentsGroupAssignment = this.agentsGroupAssignment;
            //}
            CbsConstraint.fullyEqual = true;
            other.setInvalid(allConstraintsForNode);

            while (current.depth > 0)
            {
                  
                //if (this.agentsGroupAssignment[current.prev.conflict.agentAMove] != this.agentsGroupAssignment[current.prev.conflict.agentBMove])
                //{
                    //current.constraint.group = (byte)this.agentsGroupAssignment[current.constraint.getAgentNum()];
                    if (allConstraintsForNode.Contains(current.constraint) == false)
                    {
                        //if (OtherAgentsGroupAssignment != null)
                        //    other.agentsGroupAssignment = OtherAgentsGroupAssignment;
                        CbsConstraint.fullyEqual = false;
                        return false;
                    }
                //}
                current = current.prev;
            }
            //if (OtherAgentsGroupAssignment != null)
            //    other.agentsGroupAssignment = OtherAgentsGroupAssignment;
            CbsConstraint.fullyEqual = false;
            return true;
        }

        public void clear()
        {
            this.allSingleAgentPlans = null;
        }

        public int CompareTo(IBinaryHeapItem item)
        {
            CbsNode other = (CbsNode)item;

            if (this.totalCost < other.totalCost)
                return -1;
            if (this.totalCost > other.totalCost)
                return 1;
            if (this.externalConflictsCount < other.externalConflictsCount)
                return -1;
            if (this.externalConflictsCount > other.externalConflictsCount)
                return 1;
            if (this.conflict == null)
                return -1;
            if (other.conflict == null)
                return 1;
            if (this.depth > other.depth)
                return -1;
            if (this.depth < other.depth)
                return 1;
            return 0;
        }

        private void setInvalid(HashSet<CbsConstraint> constraints)
        {
            constraints.Clear();
            CbsNode current = this;
            while (current.depth > 0)
            {
                //current.constraint.group = (byte)this.agentsGroupAssignment[current.constraint.getAgentNum()];
                constraints.Add(current.constraint);
                current = current.prev;
            }
        }

        private void setMustConstraints(List<CbsConstraint> constraints)
        {
            constraints.Clear();
            CbsNode current = this;
            while (current.depth > 0)
            {
                if (current.unConstraint != null)
                    constraints.Add(current.unConstraint);
                current = current.prev;
            }
            constraints.Sort();
        }

        //private void setInvalid(HashSet<CbsConstraint> constraints, int group, ProblemInstance problem)
        //{
        //    constraints.Clear();
        //    CbsNode current = this;
        //    while (current.depth > 0)
        //    {
        //        if (this.agentsGroupAssignment[current.constraint.getAgentNum()] == group)
        //        {
        //            if (this.agentsGroupAssignment[current.prev.conflict.agentAMove] != this.agentsGroupAssignment[current.prev.conflict.agentBMove]) 
        //                constraints.Add(current.constraint);
        //        }
        //        current = current.prev;
        //    }
        //}

        /// <summary>
        /// BH_Item implementation
        /// </summary>
        /// <returns></returns>
        public int getIndexInHeap() { return binaryHeapIndex; }
        /// <summary>
        /// BH_Item implementation
        /// </summary>
        /// <returns></returns>
        public void setIndexInHeap(int index) { binaryHeapIndex = index; }

        public Plan CalculateJointPlan()
        {
            IList<SinglePlan> plans = new List<SinglePlan>();

            for (int i = 0; i < allSingleAgentPlans.Length; i++)
            {
                plans.Add(allSingleAgentPlans[i]);
            }
            return new Plan(plans);
        }

        public int getAgentsSolutionDepth(int agent)
        {
            return allSingleAgentPlans[agent].GetSize() - 1;
        }

        public bool checkMergeCondition(int megreThreshold)
        {
            int countConflicts = 1;
            int firstGroupNumber = this.agentsGroupAssignment[conflict.agentA];
            int secondGroupNumber = this.agentsGroupAssignment[conflict.agentB];
            List<int> firstGroup = new List<int>();
            List<int> secondGroup = new List<int>();

            for (int i = 0; i < agentsGroupAssignment.Length; i++)
            {
                if (agentsGroupAssignment[i] == firstGroupNumber)
                    firstGroup.Add(i);
                if (agentsGroupAssignment[i] == secondGroupNumber)
                    secondGroup.Add(i);
            }

            CbsNode current = this.prev;
            int a,b;
            while (current != null)
            {
                a=current.conflict.agentA;
                b=current.conflict.agentB;
                if ((firstGroup.Contains(a) && secondGroup.Contains(b)) || (firstGroup.Contains(b) && secondGroup.Contains(a)))
                    countConflicts++;
                current = current.prev;
            }
            if (countConflicts > megreThreshold)
            {
                mergeGroups(firstGroupNumber, secondGroupNumber);
                return true;
            }

            return false;
        }

        public bool checkMergeCondition(int megreThreshold, int[][] globalConflictCounter)
        {
            int conflictCounter = 0;
            int firstGroupNumber = this.agentsGroupAssignment[conflict.agentA];
            int secondGroupNumber = this.agentsGroupAssignment[conflict.agentB];
            List<int> firstGroup = new List<int>();
            List<int> secondGroup = new List<int>();

            for (int i = 0; i < agentsGroupAssignment.Length; i++)
            {
                if (agentsGroupAssignment[i] == firstGroupNumber)
                    firstGroup.Add(i);
                if (agentsGroupAssignment[i] == secondGroupNumber)
                    secondGroup.Add(i);
            }

            foreach (int a in firstGroup)
            {
                foreach (int b in secondGroup)
                {
                    conflictCounter += globalConflictCounter[Math.Max(a, b)][Math.Min(a, b)];
                }
            }
            if (conflictCounter > megreThreshold)
            {
                mergeGroups(firstGroupNumber, secondGroupNumber);
                return true;
            }

            return false;
        }

        private void mergeGroups(int a, int b)
        {
            if (b < a)
            {
                int c = a;
                a = b;
                b = c;
            }
            for (int i = 0; i < agentsGroupAssignment.Length; i++)
            {
                if (agentsGroupAssignment[i] == b)
                {
                    agentsGroupAssignment[i] = (ushort)a;
                }
            }
           // normalizeGroups();
        }

        public void printPlan()
        {
            Debug.WriteLine("Plan:");
            for (int i = 0; i < allSingleAgentPlans.Length; i++)
            {
                Debug.WriteLine("agents - " + i);
                allSingleAgentPlans[i].PrintPlan();
            }
            Debug.WriteLine("End Plan");
        }

        public void printConflict()
        {
            if (conflict != null)
            {
                Debug.WriteLine("Conflict:");
                Debug.WriteLine("Agents:({0},{1})",conflict.agentA,conflict.agentB);
                Debug.WriteLine("Location:({0},{1})",conflict.agentAmove.x,conflict.agentAmove.y);
                Debug.WriteLine("Time:{0}",conflict.timeStep);
            }
            Debug.WriteLine("");
        }

        public int pathLength(int agent)
        {
            Move[] moves = allSingleAgentPlans[agent].locationsAtTime;
            Move goal = moves[moves.Length - 1];
            for (int i = moves.Length - 2; i >= 0; i--)
            {
                if (!moves[i].Equals(goal))
                    return i+1;
            }
            return 0;
        }

        public bool isAllowedConstraint(CbsConstraint check)
        {
            CbsNode current = this;
            while (current != null)
            {
                if (current.unConstraint != null && !current.unConstraint.allows(check))
                    return false;
                current = current.prev;
            }
            return true;
        }

        public void setUnConstraint(CbsConstraint set)
        {
            this.unConstraint = set;
        }

        public bool rePlan3b(ProblemInstance problem, Run runner, int agentForRePlan, int depthToReplan, ICbsSolver highLevelSolver, ICbsSolver lowLevelSolver, ref int highLevelExpanded, ref int highLevelGenerated, ref int loweLevelExpanded, ref int loweLevelGenerated)
        {
            HashSet<TimedMove> newInternalCAT = (HashSet<TimedMove>)problem.parameters[CBS_LocalConflicts.NEW_INTERNAL_CAT];
            HashSet<CbsConstraint> newConstraints = (HashSet<CbsConstraint>)problem.parameters[CBS_LocalConflicts.NEW_CONSTRAINTS];
            HashSet_U<TimedMove> InternalCAT = (HashSet_U<TimedMove>)problem.parameters[CBS_LocalConflicts.INTERNAL_CAT];
            HashSet_U<CbsConstraint> Constraints = (HashSet_U<CbsConstraint>)problem.parameters[CBS_LocalConflicts.CONSTRAINTS];
            List<CbsConstraint> mustConstraints = (List<CbsConstraint>)problem.parameters[CBS_LocalConflicts.CONSTRAINTSP];
            ICbsSolver solver = highLevelSolver;

            //this.setInvalid(newConstraints, this.agentsGroupAssignment[agentForRePlan]);
            this.setInvalid(newConstraints);
            this.setMustConstraints(mustConstraints);
            newInternalCAT.Clear();

            int groupNum = this.agentsGroupAssignment[agentForRePlan];
            List<AgentState> subGroup = new List<AgentState>();

            //Debug.WriteLine("Sub-problem:");

            for (int i = 0; i < agentsGroupAssignment.Length; i++)
            {
                if (this.agentsGroupAssignment[i] == groupNum)
                {
                    subGroup.Add(problem.m_vAgents[i]);
                    // Debug.WriteLine(i);
                }
                else
                    allSingleAgentPlans[i].addPlanToHashSet(newInternalCAT, totalCost);
            }

            replanSize = (ushort)subGroup.Count;

            if (subGroup.Count == 1)
                solver = lowLevelSolver;

            ProblemInstance subProblem = problem.Subproblem(subGroup.ToArray());
            subProblem.parameters = problem.parameters;

            InternalCAT.Join(newInternalCAT);
            Constraints.Join(newConstraints);

            //Constraints.print();

            solver.Setup(subProblem, depthToReplan);
            if (solver.Solve(runner) == false)
            {
                highLevelExpanded += solver.getHighLevelExpanded();
                highLevelGenerated += solver.getHighLevelGenerated();
                loweLevelExpanded += solver.getLowLevelExpanded();
                loweLevelGenerated += solver.getLowLevelGenerated();
                InternalCAT.Seperate(newInternalCAT);
                Constraints.Seperate(newConstraints);
                return false;
            }
            highLevelExpanded += solver.getHighLevelExpanded();
            highLevelGenerated += solver.getHighLevelGenerated();
            loweLevelExpanded += solver.getLowLevelExpanded();
            loweLevelGenerated += solver.getLowLevelGenerated();



            int j = 0;
            SinglePlan[] singlePlans = solver.getSinglePlans();

            for (int i = 0; i < agentsGroupAssignment.Length; i++)
            {
                if (this.agentsGroupAssignment[i] == groupNum)
                {
                    this.allSingleAgentPlans[i] = singlePlans[j];
                    j++;
                }

            }

            // printPlan();

            totalCost = 0;
            foreach (SinglePlan plan in allSingleAgentPlans)
            {
                totalCost += (ushort)(plan.GetSize() - 1);
            }
            InternalCAT.Seperate(newInternalCAT);
            Constraints.Seperate(newConstraints);
            newConstraints.Clear();
            this.FindConflict(problem);
            // printConflict();
            return true;
        }
    }
}