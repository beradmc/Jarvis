namespace JarvisCSharp.Services.Automation
{
    /// <summary>
    /// Service for classifying action risk levels and enforcing safety confirmations.
    /// Protects against destructive operations and manages user confirmations.
    /// </summary>
    public interface ISafetyGuardianService
    {
        /// <summary>
        /// Classify action risk level.
        /// </summary>
        /// <param name="actionType">Type of action to classify</param>
        /// <param name="parameters">Action parameters</param>
        /// <param name="targetWindow">Optional target window for context</param>
        /// <returns>Risk level classification</returns>
        RiskLevel ClassifyAction(
            string actionType,
            Dictionary<string, object> parameters,
            WindowInfo? targetWindow = null
        );

        /// <summary>
        /// Check if action requires confirmation.
        /// </summary>
        /// <param name="risk">Risk level</param>
        /// <param name="actionType">Action type</param>
        /// <returns>True if confirmation required</returns>
        bool RequiresConfirmation(RiskLevel risk, string actionType);

        /// <summary>
        /// Request user confirmation.
        /// </summary>
        /// <param name="actionDescription">Description of the action</param>
        /// <param name="risk">Risk level</param>
        /// <param name="timeoutSeconds">Timeout for user response</param>
        /// <returns>Confirmation result</returns>
        Task<ConfirmationResult> RequestConfirmationAsync(
            string actionDescription,
            RiskLevel risk,
            int timeoutSeconds = 45
        );

        /// <summary>
        /// Check if action is in whitelist (pre-approved).
        /// </summary>
        /// <param name="actionType">Action type</param>
        /// <param name="targetWindow">Target window</param>
        /// <returns>True if whitelisted</returns>
        bool IsWhitelisted(string actionType, WindowInfo targetWindow);

        /// <summary>
        /// Add action to whitelist.
        /// </summary>
        /// <param name="actionType">Action type</param>
        /// <param name="windowTitle">Window title pattern</param>
        void AddToWhitelist(string actionType, string windowTitle);

        /// <summary>
        /// Simulate action in sandbox mode.
        /// </summary>
        /// <param name="actionType">Action type</param>
        /// <param name="parameters">Action parameters</param>
        /// <returns>Simulation result</returns>
        SimulationResult SimulateAction(
            string actionType,
            Dictionary<string, object> parameters
        );
    }

    public enum RiskLevel
    {
        /// <summary>
        /// Safe operations - auto-approve (read operations, window focus)
        /// </summary>
        Safe,

        /// <summary>
        /// Caution - require verbal/text "ok" or "yes" (file operations, text input)
        /// </summary>
        Caution,

        /// <summary>
        /// Destructive - require explicit "yes, I confirm" (delete, close without save, system settings)
        /// </summary>
        Destructive
    }

    public class ConfirmationResult
    {
        public bool Confirmed { get; set; }
        public string UserResponse { get; set; } = string.Empty;
        public DateTime RequestTime { get; set; }
        public DateTime? ResponseTime { get; set; }
        public bool TimedOut { get; set; }
    }

    public class SimulationResult
    {
        public string ActionDescription { get; set; } = string.Empty;
        public string ExpectedOutcome { get; set; } = string.Empty;
        public RiskLevel Risk { get; set; }
        public List<string> AffectedTargets { get; set; } = new();
    }
}
