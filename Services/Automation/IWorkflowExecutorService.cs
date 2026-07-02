namespace JarvisCSharp.Services.Automation
{
    /// <summary>
    /// Service for parsing and executing multi-step automation workflows with conditional logic.
    /// Handles workflow parsing, execution, state management, and persistence.
    /// </summary>
    public interface IWorkflowExecutorService
    {
        /// <summary>
        /// Parse natural language command into workflow steps.
        /// </summary>
        /// <param name="naturalLanguageCommand">User's command in natural language</param>
        /// <returns>Parsed workflow</returns>
        Task<Workflow> ParseWorkflowAsync(string naturalLanguageCommand);

        /// <summary>
        /// Execute a workflow.
        /// </summary>
        /// <param name="workflow">Workflow to execute</param>
        /// <param name="options">Optional execution options</param>
        /// <returns>Workflow execution result</returns>
        Task<WorkflowExecutionResult> ExecuteWorkflowAsync(
            Workflow workflow,
            ExecutionOptions? options = null
        );

        /// <summary>
        /// Save workflow for reuse.
        /// </summary>
        /// <param name="name">Workflow name</param>
        /// <param name="workflow">Workflow to save</param>
        void SaveWorkflow(string name, Workflow workflow);

        /// <summary>
        /// Load saved workflow.
        /// </summary>
        /// <param name="name">Workflow name to load</param>
        /// <returns>Workflow or null if not found</returns>
        Workflow? LoadWorkflow(string name);

        /// <summary>
        /// Replay workflow from action log.
        /// </summary>
        /// <param name="startTime">Start time of action sequence</param>
        /// <param name="endTime">End time of action sequence</param>
        /// <returns>Reconstructed workflow</returns>
        Task<Workflow> ReplayFromLogAsync(DateTime startTime, DateTime endTime);
    }

    public class Workflow
    {
        public string Id { get; set; } = Guid.NewGuid().ToString();
        public string Name { get; set; } = string.Empty;
        public List<WorkflowStep> Steps { get; set; } = new();
        public Dictionary<string, object> State { get; set; } = new();
        public bool IsIdempotent { get; set; }
    }

    public class WorkflowStep
    {
        public int StepNumber { get; set; }
        public WorkflowActionType ActionType { get; set; }
        public Dictionary<string, object> Parameters { get; set; } = new();
        public WorkflowCondition? Condition { get; set; }
        public int MaxRetries { get; set; } = 1;
        public TimeSpan Timeout { get; set; } = TimeSpan.FromSeconds(30);
        public FailureStrategy OnFailure { get; set; } = FailureStrategy.AskUser;
    }

    public enum WorkflowActionType
    {
        OpenApplication,
        ClickElement,
        TypeText,
        SendShortcut,
        WaitForElement,
        VerifyElement,
        ExtractText,
        Loop,
        Conditional
    }

    public class WorkflowCondition
    {
        public ConditionType Type { get; set; }
        public string ElementName { get; set; } = string.Empty;
        public object? ExpectedValue { get; set; }
        public ComparisonOperator Operator { get; set; }
    }

    public enum ConditionType
    {
        ElementExists,
        ElementVisible,
        ElementEnabled,
        TextContains,
        WindowActive
    }

    public enum ComparisonOperator
    {
        Equals,
        NotEquals,
        Contains,
        GreaterThan,
        LessThan
    }

    public enum FailureStrategy
    {
        AskUser,
        RetryStep,
        SkipStep,
        AbortWorkflow
    }

    public class WorkflowExecutionResult
    {
        public bool Success { get; set; }
        public string Message { get; set; } = string.Empty;
        public List<StepResult> StepResults { get; set; } = new();
        public TimeSpan TotalDuration { get; set; }
        public Dictionary<string, object> FinalState { get; set; } = new();
    }

    public class StepResult
    {
        public int StepNumber { get; set; }
        public bool Success { get; set; }
        public string Message { get; set; } = string.Empty;
        public TimeSpan Duration { get; set; }
        public int AttemptCount { get; set; }
    }

    public class ExecutionOptions
    {
        public bool SandboxMode { get; set; } = false;
        public bool VerboseLogging { get; set; } = false;
        public int GlobalTimeout { get; set; } = 300;
    }
}
