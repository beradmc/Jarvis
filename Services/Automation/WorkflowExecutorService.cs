using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using JarvisCSharp.Core;

namespace JarvisCSharp.Services.Automation
{
    public class WorkflowExecutorService : IWorkflowExecutorService
    {
        private readonly AutomationControllerService _automationController;
        private readonly SafetyGuardianService _safetyGuardian;
        private readonly ActionLog _actionLog;
        private readonly string _workflowsDir;

        // Callback for AI parsing (injected via Gemini service)
        private Func<string, Task<List<WorkflowStep>>>? _aiParseCallback;
        // Callback for asking user on failure
        private Func<string, Task<string?>>? _userPromptCallback;

        public WorkflowExecutorService(
            AutomationControllerService automationController,
            SafetyGuardianService safetyGuardian,
            ActionLog actionLog,
            Func<string, Task<List<WorkflowStep>>>? aiParseCallback = null,
            Func<string, Task<string?>>? userPromptCallback = null)
        {
            _automationController = automationController;
            _safetyGuardian = safetyGuardian;
            _actionLog = actionLog;
            _aiParseCallback = aiParseCallback;
            _userPromptCallback = userPromptCallback;

            _workflowsDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "JarvisCSharp", "workflows");
            Directory.CreateDirectory(_workflowsDir);
        }

        public async Task<Workflow> ParseWorkflowAsync(string naturalLanguageCommand)
        {
            var workflow = new Workflow
            {
                Name = naturalLanguageCommand.Length > 50
                    ? naturalLanguageCommand.Substring(0, 50) + "..."
                    : naturalLanguageCommand
            };

            if (_aiParseCallback != null)
            {
                try
                {
                    workflow.Steps = await _aiParseCallback(naturalLanguageCommand);
                    for (int i = 0; i < workflow.Steps.Count; i++)
                    {
                        workflow.Steps[i].StepNumber = i + 1;
                    }
                    return workflow;
                }
                catch (Exception ex)
                {
                    Logger.Warning($"[WorkflowExecutor] AI parse failed, using basic parser: {ex.Message}");
                }
            }

            // Fallback: basic command parsing
            workflow.Steps = ParseBasicCommands(naturalLanguageCommand);
            return workflow;
        }

        public async Task<WorkflowExecutionResult> ExecuteWorkflowAsync(Workflow workflow, ExecutionOptions? options = null)
        {
            options ??= new ExecutionOptions();
            var result = new WorkflowExecutionResult();
            var sw = Stopwatch.StartNew();

            Logger.Information($"[WorkflowExecutor] Starting workflow '{workflow.Name}' with {workflow.Steps.Count} steps");

            foreach (var step in workflow.Steps)
            {
                if (options.SandboxMode)
                {
                    var simResult = _safetyGuardian.SimulateAction(step.ActionType.ToString(), step.Parameters);
                    result.StepResults.Add(new StepResult
                    {
                        StepNumber = step.StepNumber,
                        Success = true,
                        Message = $"[SANDBOX] {simResult.ActionDescription}",
                        Duration = TimeSpan.Zero,
                        AttemptCount = 1
                    });
                    continue;
                }

                // Check condition if present
                if (step.Condition != null)
                {
                    bool conditionMet = await EvaluateConditionAsync(step.Condition, workflow.State);
                    if (!conditionMet)
                    {
                        result.StepResults.Add(new StepResult
                        {
                            StepNumber = step.StepNumber,
                            Success = true,
                            Message = "Skipped (condition not met)",
                            AttemptCount = 0
                        });
                        continue;
                    }
                }

                // Handle loop steps
                if (step.ActionType == WorkflowActionType.Loop)
                {
                    var loopResult = await ExecuteLoopStepAsync(step, workflow, options);
                    result.StepResults.Add(loopResult);
                    if (!loopResult.Success && step.OnFailure == FailureStrategy.AbortWorkflow)
                    {
                        result.Success = false;
                        result.Message = $"Workflow aborted at loop step {step.StepNumber}";
                        break;
                    }
                    continue;
                }

                var stepResult = await ExecuteStepWithRetryAsync(step, workflow, options);
                result.StepResults.Add(stepResult);

                if (!stepResult.Success)
                {
                    var failureAction = await HandleFailureAsync(step, stepResult, options);
                    if (failureAction == FailureStrategy.AbortWorkflow)
                    {
                        result.Success = false;
                        result.Message = $"Workflow aborted at step {step.StepNumber}: {stepResult.Message}";
                        break;
                    }
                }
            }

            sw.Stop();
            result.TotalDuration = sw.Elapsed;
            result.FinalState = new Dictionary<string, object>(workflow.State);

            if (!result.StepResults.Any(r => !r.Success))
            {
                result.Success = true;
                result.Message = $"Workflow completed ({result.StepResults.Count} steps in {result.TotalDuration.TotalSeconds:F1}s)";
            }

            // Auto-suggest saving for workflows > 3 steps
            if (workflow.Steps.Count > 3 && result.Success)
            {
                Logger.Information($"[WorkflowExecutor] Tip: This workflow has {workflow.Steps.Count} steps. Consider saving it with SaveWorkflow().");
            }

            return result;
        }

        private async Task<StepResult> ExecuteStepWithRetryAsync(WorkflowStep step, Workflow workflow, ExecutionOptions options)
        {
            StepResult? lastResult = null;

            for (int attempt = 1; attempt <= step.MaxRetries; attempt++)
            {
                var sw = Stopwatch.StartNew();

                try
                {
                    var execResult = await ExecuteSingleStepAsync(step, workflow);
                    sw.Stop();

                    lastResult = new StepResult
                    {
                        StepNumber = step.StepNumber,
                        Success = execResult.Success,
                        Message = execResult.Message,
                        Duration = sw.Elapsed,
                        AttemptCount = attempt
                    };

                    if (execResult.Success) break;
                }
                catch (Exception ex)
                {
                    sw.Stop();
                    lastResult = new StepResult
                    {
                        StepNumber = step.StepNumber,
                        Success = false,
                        Message = ex.Message,
                        Duration = sw.Elapsed,
                        AttemptCount = attempt
                    };
                }

                if (attempt < step.MaxRetries)
                {
                    await Task.Delay(500 * attempt); // exponential-ish backoff
                }
            }

            return lastResult!;
        }

        private async Task<ExecutionResult> ExecuteSingleStepAsync(WorkflowStep step, Workflow workflow)
        {
            switch (step.ActionType)
            {
                case WorkflowActionType.OpenApplication:
                    var appName = GetParam<string>(step.Parameters, "appName", "");
                    return await _automationController.LaunchApplicationAsync(appName);

                case WorkflowActionType.ClickElement:
                    var x = GetParam<int>(step.Parameters, "x", 0);
                    var y = GetParam<int>(step.Parameters, "y", 0);
                    var clickType = GetParam<string>(step.Parameters, "clickType", "LeftSingle");
                    Enum.TryParse<ClickType>(clickType, out var ct);
                    return await _automationController.ClickAsync(new System.Drawing.Point(x, y), ct);

                case WorkflowActionType.TypeText:
                    var text = GetParam<string>(step.Parameters, "text", "");
                    return await _automationController.TypeTextAsync(text);

                case WorkflowActionType.SendShortcut:
                    var shortcut = GetParam<string>(step.Parameters, "shortcut", "");
                    return await _automationController.SendShortcutAsync(shortcut);

                case WorkflowActionType.WaitForElement:
                    var waitMs = GetParam<int>(step.Parameters, "waitMs", 2000);
                    await Task.Delay(waitMs);
                    return new ExecutionResult { Success = true, Message = $"Waited {waitMs}ms" };

                case WorkflowActionType.ExtractText:
                    // Delegate to vision/OCR (placeholder)
                    return new ExecutionResult { Success = true, Message = "Text extraction delegated" };

                default:
                    return new ExecutionResult { Success = false, Message = $"Unknown action type: {step.ActionType}" };
            }
        }

        private async Task<bool> EvaluateConditionAsync(WorkflowCondition condition, Dictionary<string, object> state)
        {
            // Basic condition evaluation using state dictionary
            switch (condition.Type)
            {
                case ConditionType.ElementExists:
                    return state.ContainsKey($"element:{condition.ElementName}");

                case ConditionType.TextContains:
                    if (state.TryGetValue($"text:{condition.ElementName}", out var textVal))
                    {
                        return textVal?.ToString()?.Contains(condition.ExpectedValue?.ToString() ?? "") ?? false;
                    }
                    return false;

                case ConditionType.WindowActive:
                    return state.ContainsKey($"window:{condition.ElementName}");

                default:
                    return true;
            }
        }

        private async Task<StepResult> ExecuteLoopStepAsync(WorkflowStep step, Workflow workflow, ExecutionOptions options)
        {
            var iterations = GetParam<int>(step.Parameters, "count", 1);
            var sw = Stopwatch.StartNew();

            for (int i = 0; i < iterations; i++)
            {
                // Execute inner steps if any (for now, execute the main action)
                if (step.Parameters.ContainsKey("innerAction"))
                {
                    var innerStep = new WorkflowStep
                    {
                        StepNumber = step.StepNumber,
                        ActionType = Enum.TryParse<WorkflowActionType>(
                            GetParam<string>(step.Parameters, "innerAction", ""), out var at) ? at : WorkflowActionType.ClickElement,
                        Parameters = step.Parameters
                    };
                    await ExecuteSingleStepAsync(innerStep, workflow);
                }
                await Task.Delay(200);
            }

            sw.Stop();
            return new StepResult
            {
                StepNumber = step.StepNumber,
                Success = true,
                Message = $"Loop completed ({iterations} iterations)",
                Duration = sw.Elapsed,
                AttemptCount = 1
            };
        }

        private async Task<FailureStrategy> HandleFailureAsync(WorkflowStep step, StepResult result, ExecutionOptions options)
        {
            switch (step.OnFailure)
            {
                case FailureStrategy.AbortWorkflow:
                    return FailureStrategy.AbortWorkflow;

                case FailureStrategy.SkipStep:
                    Logger.Warning($"[WorkflowExecutor] Skipping failed step {step.StepNumber}: {result.Message}");
                    return FailureStrategy.SkipStep;

                case FailureStrategy.RetryStep:
                    return FailureStrategy.RetryStep;

                case FailureStrategy.AskUser:
                    if (_userPromptCallback != null)
                    {
                        var response = await _userPromptCallback(
                            $"Adım {step.StepNumber} başarısız oldu: {result.Message}\n" +
                            "Seçenekler: [tekrar] dene / [atla] / [iptal]");

                        var r = response?.Trim().ToLower() ?? "iptal";
                        if (r.Contains("tekrar") || r.Contains("retry")) return FailureStrategy.RetryStep;
                        if (r.Contains("atla") || r.Contains("skip")) return FailureStrategy.SkipStep;
                    }
                    return FailureStrategy.AbortWorkflow;

                default:
                    return FailureStrategy.AbortWorkflow;
            }
        }

        public void SaveWorkflow(string name, Workflow workflow)
        {
            var path = Path.Combine(_workflowsDir, $"{SanitizeFileName(name)}.json");
            var json = JsonSerializer.Serialize(workflow, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(path, json);
            Logger.Information($"[WorkflowExecutor] Saved workflow: {name}");
        }

        public Workflow? LoadWorkflow(string name)
        {
            var path = Path.Combine(_workflowsDir, $"{SanitizeFileName(name)}.json");
            if (!File.Exists(path)) return null;

            var json = File.ReadAllText(path);
            return JsonSerializer.Deserialize<Workflow>(json);
        }

        public async Task<Workflow> ReplayFromLogAsync(DateTime startTime, DateTime endTime)
        {
            var entries = _actionLog.GetLogRange(startTime, endTime);
            var workflow = new Workflow
            {
                Name = $"Replay {startTime:HH:mm} - {endTime:HH:mm}"
            };

            int stepNum = 1;
            foreach (var entry in entries.Where(e => e.Success))
            {
                var actionType = Enum.TryParse<WorkflowActionType>(entry.ActionType, out var at)
                    ? at : WorkflowActionType.ClickElement;

                workflow.Steps.Add(new WorkflowStep
                {
                    StepNumber = stepNum++,
                    ActionType = actionType,
                    Parameters = entry.Parameters
                });
            }

            return workflow;
        }

        // --- Basic command parser (fallback when AI is unavailable) ---
        private List<WorkflowStep> ParseBasicCommands(string command)
        {
            var steps = new List<WorkflowStep>();
            // Split by "and then", "sonra", "ve"
            var parts = command.Split(new[] { " and then ", " ve sonra ", " sonra " }, StringSplitOptions.RemoveEmptyEntries);

            int stepNum = 1;
            foreach (var part in parts)
            {
                var trimmed = part.Trim().ToLower();
                var step = new WorkflowStep { StepNumber = stepNum++ };

                if (trimmed.StartsWith("aç ") || trimmed.StartsWith("open "))
                {
                    step.ActionType = WorkflowActionType.OpenApplication;
                    step.Parameters["appName"] = trimmed.Replace("aç ", "").Replace("open ", "").Trim();
                }
                else if (trimmed.StartsWith("yaz ") || trimmed.StartsWith("type "))
                {
                    step.ActionType = WorkflowActionType.TypeText;
                    step.Parameters["text"] = trimmed.Replace("yaz ", "").Replace("type ", "").Trim();
                }
                else if (trimmed.Contains("ctrl+") || trimmed.Contains("alt+") || trimmed.Contains("shift+"))
                {
                    step.ActionType = WorkflowActionType.SendShortcut;
                    step.Parameters["shortcut"] = trimmed;
                }
                else if (trimmed.StartsWith("tıkla ") || trimmed.StartsWith("click "))
                {
                    step.ActionType = WorkflowActionType.ClickElement;
                    step.Parameters["elementName"] = trimmed.Replace("tıkla ", "").Replace("click ", "").Trim();
                }
                else if (trimmed.StartsWith("bekle ") || trimmed.StartsWith("wait "))
                {
                    step.ActionType = WorkflowActionType.WaitForElement;
                    if (int.TryParse(new string(trimmed.Where(char.IsDigit).ToArray()), out var ms))
                    {
                        step.Parameters["waitMs"] = ms > 100 ? ms : ms * 1000;
                    }
                    else
                    {
                        step.Parameters["waitMs"] = 2000;
                    }
                }
                else
                {
                    step.ActionType = WorkflowActionType.ClickElement;
                    step.Parameters["elementName"] = trimmed;
                }

                steps.Add(step);
            }

            return steps;
        }

        private T GetParam<T>(Dictionary<string, object> parameters, string key, T defaultValue)
        {
            if (parameters.TryGetValue(key, out var val))
            {
                try
                {
                    if (val is JsonElement jsonElement)
                    {
                        if (typeof(T) == typeof(string)) return (T)(object)jsonElement.GetString()!;
                        if (typeof(T) == typeof(int)) return (T)(object)jsonElement.GetInt32();
                        if (typeof(T) == typeof(bool)) return (T)(object)jsonElement.GetBoolean();
                    }
                    return (T)Convert.ChangeType(val, typeof(T));
                }
                catch { }
            }
            return defaultValue;
        }

        private string SanitizeFileName(string name)
        {
            foreach (var c in Path.GetInvalidFileNameChars())
            {
                name = name.Replace(c, '_');
            }
            return name;
        }
    }
}
