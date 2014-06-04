﻿using System;
using System.Collections.Generic;
using System.IO;
using System.Diagnostics;


namespace CPF_experiment
{
    /// <summary>
    /// Cbs with no closed list. According to the paper, it's usually a bit faster since there are very few cycles in the search.
    /// </summary>
    class CBS_NoDD : ISolver
    {
        protected ProblemInstance instance;
        public OpenList openList;
        public int highLevelExpanded;
        public int highLevelGenerated;
        public int totalCost;
        protected Run runner;
        protected CbsNode goalNode;
        protected Plan solution;
        protected int maxCost;
        protected ICbsSolver solver;
        protected ICbsSolver lowLevelSolver;
        protected int mergeThreshold;
        protected int minCost;
        protected int maxThreshold;
        protected int maxSizeGroup;
        protected HeuristicCalculator heuristic;
        int[][] globalConflictsCounter;
        bool topMost;

        public CBS_NoDD(ICbsSolver solver, int maxThreshold = -1, int currentThreshold = -1)
        {
            this.openList = new OpenList(this);
            this.mergeThreshold = currentThreshold;
            this.solver = solver;
            this.lowLevelSolver = solver;
            this.maxThreshold = maxThreshold;
            if (currentThreshold < maxThreshold)
            {
                this.solver = new CBS_GlobalConflicts(solver, maxThreshold, currentThreshold + 1);
            }
        }

        public void Clear()
        {
            this.openList.Clear();
            this.instance = null;
            this.solver.Clear();
        }

        public int GetSolutionCost() { return this.totalCost; }

        public virtual void OutputStatisticsHeader(TextWriter output)
        {
            output.Write(this.ToString() + " Expanded (HL)");
            output.Write(Run.RESULTS_DELIMITER);
            output.Write(this.ToString() + " Generated (HL)");
            output.Write(Run.RESULTS_DELIMITER);

            this.solver.OutputStatisticsHeader(output);
        }

        public void OutputStatistics(TextWriter output)
        {
            Console.WriteLine("Total Expanded Nodes (High-Level): {0}", this.GetHighLevelExpanded());
            Console.WriteLine("Total Generated Nodes (High-Level): {0}", this.GetHighLevelGenerated());

            output.Write(this.highLevelExpanded + Run.RESULTS_DELIMITER);
            output.Write(this.highLevelGenerated + Run.RESULTS_DELIMITER);

            this.solver.OutputAccumulatedStatistics(output);
        }

        public virtual int NumStatsColumns
        {
            get
            {
                return 2 + this.solver.NumStatsColumns;
            }
        }

        public void ClearStatistics()
        {
            if (this.topMost)
                this.solver.ClearAccumulatedStatistics();
            // Own statistics cleared on Setup.
            this.openList.ClearStatistics();
        }

        public bool Solve()
        {
            //Debug.WriteLine("Solving Sub-problem On Level - " + mergeThreshold);

            CbsConflict conflict;
            CbsNode currentNode = (CbsNode)openList.Remove();
            
            highLevelExpanded++;
            if (currentNode.Solve(minCost) == false)
            {
                return false;
            }
            if (currentNode.totalCost <= this.maxCost)
            {
                this.openList.Add(currentNode);
                this.addToGlobalConflictCount(currentNode.GetConflict());
            }
            while (openList.Count > 0 && runner.ElapsedMilliseconds() < Constants.MAX_TIME)
            {
                // Check if max time has been exceeded
                if (runner.ElapsedMilliseconds() > Constants.MAX_TIME)
                {
                    totalCost = Constants.TIMEOUT_COST;
                    Console.WriteLine("Out of time");
                    this.Clear();
                    return false;
                }
                currentNode = (CbsNode)openList.Remove();

                conflict = currentNode.GetConflict();
                // Check if node is the goal
                if (conflict == null)
                {
                    this.totalCost = currentNode.totalCost;
                    this.goalNode = currentNode;
                    this.solution = currentNode.CalculateJointPlan();
                    this.Clear();
                    return true;
                }
                // Expand
                highLevelExpanded++;
                if (Expand(currentNode, conflict))
                    if (currentNode.agentAExpansion == CbsNode.ExpansionState.EXPANDED &&
                        currentNode.agentBExpansion == CbsNode.ExpansionState.EXPANDED) // Fully expanded
                        currentNode.Clear();
            }
            totalCost = Constants.NO_SOLUTION_COST;
            this.Clear();
            return false;
        }

        public virtual bool Expand(CbsNode node,CbsConflict conflict)
        {

            if (this.maxThreshold != -1 && MergeConflicting(node))
            {
                bool solved = node.Replan(conflict.agentA, this.minCost);
                this.maxSizeGroup = Math.Max(this.maxSizeGroup, node.replanSize);
                if (solved == false)
                    return true;
                if (node.totalCost <= maxCost)
                    openList.Add(node);
                this.addToGlobalConflictCount(node.GetConflict());
                return false;
            }


             CbsConstraint con;
             CbsNode toAdd;
            if (node.agentAExpansion == CbsNode.ExpansionState.NOT_EXPANDED &&
                Math.Max(minCost, conflict.timeStep) > node.PathLength(conflict.agentA))
            {
                node.agentAExpansion = CbsNode.ExpansionState.DEFERRED;
                node.totalCost += (ushort)(conflict.timeStep + 1 - node.PathLength(conflict.agentA));
                openList.Add(node);
            }
            else
            {
                con = new CbsConstraint(conflict, instance, true);
                toAdd = new CbsNode(node, con, conflict.agentA);

                if (toAdd.Replan(conflict.agentA, Math.Max(minCost, conflict.timeStep)))
                {
                    if (toAdd.totalCost <= this.maxCost)
                    {
                        openList.Add(toAdd);
                        this.highLevelGenerated++;
                        addToGlobalConflictCount(toAdd.GetConflict());
                    }
                }
                
                node.agentAExpansion = CbsNode.ExpansionState.EXPANDED;
            }

            if (node.agentBExpansion == CbsNode.ExpansionState.NOT_EXPANDED &&
                Math.Max(minCost, conflict.timeStep) > node.PathLength(conflict.agentB))
            {
                if (node.agentAExpansion == CbsNode.ExpansionState.DEFERRED)
                    throw new Exception("Expand/CBS");
                node.agentBExpansion = CbsNode.ExpansionState.DEFERRED;
                node.totalCost += (ushort)(conflict.timeStep + 1 - node.PathLength(conflict.agentB));
                openList.Add(node);
            }
            else
            {
                con = new CbsConstraint(conflict, instance, false);
                toAdd = new CbsNode(node, con, conflict.agentB);

                if (toAdd.Replan(conflict.agentB, Math.Max(minCost, conflict.timeStep)))
                {
                    if (toAdd.totalCost <= this.maxCost)
                    {
                        openList.Add(toAdd);
                        this.highLevelGenerated++;
                        addToGlobalConflictCount(toAdd.GetConflict());
                    }
                }

                node.agentBExpansion = CbsNode.ExpansionState.EXPANDED;
            }
            return true;
        }

        public virtual Plan GetPlan()
        {
            return this.solution;
        }

        private void PrintSolution(WorldState end)
        {
        }

        public int GetSolutionDepth() { return -1; }
        public long GetMemoryUsed() { return Process.GetCurrentProcess().VirtualMemorySize64; }
        public SinglePlan[] getSinglePlans()
        {
            return goalNode.allSingleAgentPlans;
        }
        public int GetHighLevelExpanded() { return highLevelExpanded; }
        public int GetHighLevelGenerated() { return highLevelGenerated; }
        public int GetLowLevelExpanded() { return this.solver.GetAccumulatedExpanded(); }
        public int GetLowLevelGenerated() { return this.solver.GetAccumulatedGenerated(); }
        public int GetExpanded() { return highLevelExpanded; }
        public int GetGenerated() { return highLevelGenerated; }

        public int GetMaxGroupSize()
        {
            return this.maxSizeGroup;
        }

        public void Setup(ProblemInstance problemInstance, Run runner)
        {
            globalConflictsCounter = new int[problemInstance.m_vAgents.Length][];
            for (int i = 0; i < globalConflictsCounter.Length; i++)
            {
                globalConflictsCounter[i] = new int[i];
                for (int j = 0; j < i; j++)
                {
                    globalConflictsCounter[i][j] = 0;
                }
            }
            this.instance = problemInstance;
            this.runner = runner;
            CbsNode root = new CbsNode(instance.m_vAgents.Length, problemInstance, this.solver, this.lowLevelSolver, runner);
            this.openList.Add(root);
            this.highLevelExpanded = 0;
            this.highLevelGenerated = 1;
            maxSizeGroup = 1;
            this.totalCost = 0;

            if (problemInstance.parameters.ContainsKey(Trevor.MAXIMUM_COST_KEY))
                this.maxCost = (int)(problemInstance.parameters[Trevor.MAXIMUM_COST_KEY]);
            else
                this.maxCost = int.MaxValue;

            if (problemInstance.parameters.ContainsKey(CBS_LocalConflicts.INTERNAL_CAT) == false)
            {
                problemInstance.parameters[CBS_LocalConflicts.INTERNAL_CAT] = new HashSet_U<TimedMove>();
                problemInstance.parameters[CBS_LocalConflicts.CONSTRAINTS] = new HashSet_U<CbsConstraint>();
                this.topMost = true;
            }
            else
                this.topMost = false;

            minCost = 0;
        }

        public void SetHeuristic(HeuristicCalculator heuristic)
        {
            this.heuristic = heuristic;
            this.solver.SetHeuristic(heuristic);
        }

        public HeuristicCalculator GetHeuristic()
        {
            return this.heuristic;
        }

        protected bool MergeConflicting(CbsNode node)
        {
            return node.MergeIf(mergeThreshold, globalConflictsCounter);
        }

        protected void addToGlobalConflictCount(CbsConflict conflict)
        {
            if (conflict != null)
                globalConflictsCounter[Math.Max(conflict.agentA, conflict.agentB)][Math.Min(conflict.agentA, conflict.agentB)]++;
        }

        public string GetName()
        {
            if (mergeThreshold == -1)
                return "Basic CBS NoDD";
            return "CBS Global NoDD(" + mergeThreshold + ")(" + maxThreshold + ")+" + lowLevelSolver;
        }
    }

    class CBS_NoDDb3 : ISolver
    {

        protected ProblemInstance instance;
        public OpenList openList;
        public int highLevelExpanded;
        public int highLevelGenerated;
        public int totalCost;
        protected Run runner;
        protected CbsNode goalNode;
        protected Plan solution;
        protected int maxCost;
        protected ICbsSolver solver;
        protected ICbsSolver lowLevelSolver;
        protected HeuristicCalculator heuristic;
        protected int mergeThreshold;
        protected int minCost;
        protected int maxThreshold;
        protected int maxSizeGroup;
        int[][] globalConflictsCounter;
        bool topMost;

        public CBS_NoDDb3(ICbsSolver solver, int maxThreshold = -1, int currentThreshold = -1)
        {
            this.openList = new OpenList(this);
            this.mergeThreshold = currentThreshold;
            this.solver = solver;
            this.lowLevelSolver = solver;
            this.maxThreshold = maxThreshold;
            if (currentThreshold < maxThreshold)
            {
                this.solver = new CBS_GlobalConflicts(solver, maxThreshold, currentThreshold + 1);
            }
        }

        public void Clear()
        {
            this.openList.Clear();
            this.instance = null;
            this.solver.Clear();
        }

        public int GetSolutionCost() { return this.totalCost; }

        public virtual void OutputStatisticsHeader(TextWriter output)
        {
            output.Write(this.ToString() + " Expanded (HL)");
            output.Write(Run.RESULTS_DELIMITER);
            output.Write(this.ToString() + " Generated (HL)");
            output.Write(Run.RESULTS_DELIMITER);

            this.solver.OutputStatisticsHeader(output);
        }

        public void OutputStatistics(TextWriter output)
        {
            Console.WriteLine("Total Expanded Nodes (High-Level): {0}", this.GetHighLevelExpanded());
            Console.WriteLine("Total Generated Nodes (High-Level): {0}", this.GetHighLevelGenerated());
            
            output.Write(this.highLevelExpanded + Run.RESULTS_DELIMITER);
            output.Write(this.highLevelGenerated + Run.RESULTS_DELIMITER);

            this.solver.OutputAccumulatedStatistics(output);
        }

        public virtual int NumStatsColumns
        {
            get
            {
                return 2 + this.solver.NumStatsColumns;
            }
        }

        public void ClearStatistics()
        {
            if (this.topMost)
                this.solver.ClearAccumulatedStatistics();
            // Own statistics cleared on Setup.
            this.openList.ClearStatistics();
        }

        public bool Solve()
        {
            //Debug.WriteLine("Solving Sub-problem On Level - " + mergeThreshold);

            CbsConflict conflict;
            CbsNode currentNode = (CbsNode)openList.Remove();

            highLevelExpanded++;
            if (currentNode.Solve(minCost) == false)
            {
                return false;
            }
            if (currentNode.totalCost <= this.maxCost)
            {
                this.openList.Add(currentNode);
                this.addToGlobalConflictCount(currentNode.GetConflict());
            }
            while (openList.Count > 0 && runner.ElapsedMilliseconds() < Constants.MAX_TIME)
            {
                // Check if max time has been exceeded
                if (runner.ElapsedMilliseconds() > Constants.MAX_TIME)
                {
                    totalCost = Constants.TIMEOUT_COST;
                    Console.WriteLine("Out of time");
                    this.Clear();
                    return false;
                }
                currentNode = (CbsNode)openList.Remove();

                conflict = currentNode.GetConflict();
                // Check if node is the goal
                if (conflict == null)
                {
                    this.totalCost = currentNode.totalCost;
                    this.goalNode = currentNode;
                    this.solution = currentNode.CalculateJointPlan();
                    this.Clear();
                    return true;
                }
                // Expand
                highLevelExpanded++;
                if (Expand(currentNode, conflict))
                    if (currentNode.agentAExpansion == CbsNode.ExpansionState.EXPANDED &&
                        currentNode.agentBExpansion == CbsNode.ExpansionState.EXPANDED) // Fully expanded
                        currentNode.Clear();
            }
            totalCost = Constants.NO_SOLUTION_COST;
            this.Clear();
            return false;
        }

        public virtual bool Expand(CbsNode node, CbsConflict conflict)
        {
            if (this.maxThreshold != -1 && MergeConflicting(node))
            {
                if (node.Replan(conflict.agentA, this.minCost) == false)
                {
                    if (node.replanSize > this.maxSizeGroup)
                        maxSizeGroup = node.replanSize;
                    return true;
                }
                if (node.replanSize > this.maxSizeGroup)
                    maxSizeGroup = node.replanSize;
                if (node.totalCost <= maxCost)
                    openList.Add(node);
                this.addToGlobalConflictCount(node.GetConflict());
                return false;
            }


            CbsNode toAdd;
            CbsConstraint con2 = new CbsConstraint(conflict, instance, false);
            CbsConstraint con1 = new CbsConstraint(conflict, instance, true);
            if (node.agentAExpansion == CbsNode.ExpansionState.NOT_EXPANDED &&
                Math.Max(minCost, conflict.timeStep) > node.PathLength(conflict.agentA))
            {
                node.agentAExpansion = CbsNode.ExpansionState.DEFERRED;
                node.totalCost += (ushort)(conflict.timeStep + 1 - node.PathLength(conflict.agentA));
                openList.Add(node);
            }
            else 
            {
                if (node.DoesMustConstraintAllow(con1))
                {
                    toAdd = new CbsNode(node, con1, conflict.agentA);
                    toAdd.SetMustConstraint(con2);

                    if (toAdd.Replan(conflict.agentA, Math.Max(minCost, conflict.timeStep)))
                    {
                        if (toAdd.totalCost <= this.maxCost)
                        {
                            openList.Add(toAdd);
                            this.highLevelGenerated++;
                            addToGlobalConflictCount(toAdd.GetConflict());
                        }
                    }
                }
                
                node.agentAExpansion = CbsNode.ExpansionState.EXPANDED;
            }
            if (node.agentBExpansion == CbsNode.ExpansionState.NOT_EXPANDED &&
                Math.Max(minCost, conflict.timeStep) > node.PathLength(conflict.agentB))
            {
                if (node.agentAExpansion == CbsNode.ExpansionState.DEFERRED)
                    throw new Exception("Expand/CBS");
                node.agentBExpansion = CbsNode.ExpansionState.DEFERRED;
                node.totalCost += (ushort)(conflict.timeStep + 1 - node.PathLength(conflict.agentB));
                openList.Add(node);
            }
            else 
            {
                if (node.DoesMustConstraintAllow(con2))
                {
                    toAdd = new CbsNode(node, con2, conflict.agentB);
                    toAdd.SetMustConstraint(con1);

                    if (toAdd.Replan(conflict.agentB, Math.Max(minCost, conflict.timeStep)))
                    {
                        if (toAdd.totalCost <= this.maxCost)
                        {
                            openList.Add(toAdd);
                            this.highLevelGenerated++;
                            addToGlobalConflictCount(toAdd.GetConflict());
                        }
                    }
                }
                node.agentBExpansion = CbsNode.ExpansionState.EXPANDED;
            }

            if (node.agentAExpansion == CbsNode.ExpansionState.EXPANDED &&
                node.agentBExpansion == CbsNode.ExpansionState.EXPANDED)
            {
                toAdd = new CbsNode(node, con1, conflict.agentA);
                if (toAdd.Replan(conflict.agentA, Math.Max(minCost, conflict.timeStep)))
                {
                    toAdd = new CbsNode(toAdd, con2, conflict.agentB);
                    if (toAdd.Replan(conflict.agentB, Math.Max(minCost, conflict.timeStep)))
                    {
                        if (toAdd.totalCost <= this.maxCost)
                        {
                            openList.Add(toAdd);
                            this.highLevelGenerated++;
                            addToGlobalConflictCount(toAdd.GetConflict());
                        }
                    }
                }
            }
            return true;
        }

        public virtual Plan GetPlan()
        {
            return this.solution;
        }

        private void PrintSolution(WorldState end)
        {
        }

        public int GetSolutionDepth() { return -1; }
        public long GetMemoryUsed() { return Process.GetCurrentProcess().VirtualMemorySize64; }
        public SinglePlan[] getSinglePlans()
        {
            return goalNode.allSingleAgentPlans;
        }
        public int GetHighLevelExpanded() { return highLevelExpanded; }
        public int GetHighLevelGenerated() { return highLevelGenerated; }
        public int GetLowLevelExpanded() { return this.solver.GetAccumulatedExpanded(); }
        public int GetLowLevelGenerated() { return this.solver.GetAccumulatedGenerated(); }
        public int GetExpanded() { return highLevelExpanded; }
        public int GetGenerated() { return highLevelGenerated; }
        
        public int GetMaxGroupSize()
        {
            return this.maxSizeGroup;
        }

        public void Setup(ProblemInstance problemInstance, Run runner)
        {
            globalConflictsCounter = new int[problemInstance.m_vAgents.Length][];
            for (int i = 0; i < globalConflictsCounter.Length; i++)
            {
                globalConflictsCounter[i] = new int[i];
                for (int j = 0; j < i; j++)
                {
                    globalConflictsCounter[i][j] = 0;
                }
            }
            this.instance = problemInstance;
            this.runner = runner;
            CbsNode root = new CbsNode(instance.m_vAgents.Length, problemInstance, this.solver, this.lowLevelSolver, runner);
            this.openList.Add(root);
            this.highLevelExpanded = 0;
            this.highLevelGenerated = 1;
            maxSizeGroup = 1;
            this.totalCost = 0;

            if (problemInstance.parameters.ContainsKey(Trevor.MAXIMUM_COST_KEY))
                this.maxCost = (int)(problemInstance.parameters[Trevor.MAXIMUM_COST_KEY]);
            else
                this.maxCost = int.MaxValue;

            if (problemInstance.parameters.ContainsKey(CBS_LocalConflicts.INTERNAL_CAT) == false) // Top-most CBS only
            {
                problemInstance.parameters[CBS_LocalConflicts.INTERNAL_CAT] = new HashSet_U<TimedMove>();
                problemInstance.parameters[CBS_LocalConflicts.CONSTRAINTS] = new HashSet_U<CbsConstraint>();
                this.topMost = true;
            }
            else
                this.topMost = false;

            minCost = 0;
        }

        public void SetHeuristic(HeuristicCalculator heuristic) 
        {
            this.heuristic = heuristic;
            this.solver.SetHeuristic(heuristic);
        }

        public HeuristicCalculator GetHeuristic()
        {
            return this.heuristic;
        }

        protected bool MergeConflicting(CbsNode node)
        {
            return node.MergeIf(mergeThreshold, globalConflictsCounter);
        }

        protected void addToGlobalConflictCount(CbsConflict conflict)
        {
            if (conflict != null)
                globalConflictsCounter[Math.Max(conflict.agentA, conflict.agentB)][Math.Min(conflict.agentA, conflict.agentB)]++;
        }

        public string GetName()
        {
            if (mergeThreshold == -1)
                return "Basic CBS NoDDb3";
            return "CBS Global NoDDb3(" + mergeThreshold + ")(" + maxThreshold + ")+" + lowLevelSolver;
        }
    }
}
