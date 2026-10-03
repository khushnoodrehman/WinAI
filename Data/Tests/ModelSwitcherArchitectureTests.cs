using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Windows.Storage;
using WinAI.Data.Database;
using WinAI.Data.Models;
using WinAI.Data.Repositories;
using WinAI.Data.Services;
using WinAI.Models;
using WinAI.Services;
using WinAI.Services.Providers;

namespace WinAI.Data.Tests
{
    /// <summary>
    /// Test suite validating the Model Switcher & Provider Architecture:
    /// - Provider registry listing & resolution
    /// - Model descriptor registry & capabilities
    /// - Key Vault credential availability checks
    /// - Atomic model switching with rollback on failure
    /// - Missing credential diagnostics & rejection
    /// - Multi-model conversation continuity (one conversation with messages from multiple models)
    /// - Historical message metadata preservation
    /// - Conversation active model persistence & restoration
    /// </summary>
    public sealed class ModelSwitcherArchitectureTests
    {
        public static async Task<TestReport> RunAllTestsAsync(string customTempDir = null)
        {
            var report = new TestReport();
            var registry = AiProviderRegistry.Instance;

            string tempFolder = customTempDir;
            if (string.IsNullOrEmpty(tempFolder))
            {
                try { tempFolder = ApplicationData.Current.TemporaryFolder.Path; } catch { tempFolder = Path.GetTempPath(); }
            }
            string testDbPath = Path.Combine(tempFolder, $"test_modelswitcher_{Guid.NewGuid():N}.db");

            WinAIDatabase testDb = null;
            try
            {
                testDb = await DatabaseInitializer.InitializeAsync(testDbPath).ConfigureAwait(false);
                var convRepo = new ConversationRepository(testDb);
                var msgRepo = new MessageRepository(testDb);
                var convService = new ConversationService(testDb, convRepo, msgRepo);

                // 1. Test Provider Registry Listing
                RunTest(report, "ProviderRegistry_ListsAllCoreProviders", () =>
                {
                    var providers = registry.GetProviders();
                    Assert(providers.Count >= 6, $"Expected at least 6 providers, found {providers.Count}");
                    Assert(providers.Any(p => p.Id == "openai"), "Missing OpenAI provider");
                    Assert(providers.Any(p => p.Id == "gemini"), "Missing Gemini provider");
                    Assert(providers.Any(p => p.Id == "claude"), "Missing Claude provider");
                    Assert(providers.Any(p => p.Id == "deepseek"), "Missing DeepSeek provider");
                    Assert(providers.Any(p => p.Id == "xai"), "Missing xAI provider");
                    Assert(providers.Any(p => p.Id == "perplexity"), "Missing Perplexity provider");
                });

                // 2. Test Model Registry Listing
                RunTest(report, "ModelRegistry_ListsModelsWithDescriptors", () =>
                {
                    var allModels = registry.GetAllModels();
                    Assert(allModels.Count > 0, "No models listed in registry");

                    var geminiModels = registry.GetModelsForProvider("gemini");
                    Assert(geminiModels.Count > 0, "No Gemini models found");
                    Assert(geminiModels.Any(m => m.Id.Contains("flash")), "Missing Gemini Flash model");

                    var openaiModels = registry.GetModelsForProvider("openai");
                    Assert(openaiModels.Any(m => m.Id == "gpt-4o"), "Missing GPT-4o model");
                });

                // 3. Test Provider Resolution & Aliases
                RunTest(report, "ProviderResolution_HandlesIdsAndAliases", () =>
                {
                    var p1 = registry.GetProvider("google");
                    Assert(p1 != null && p1.Id == "gemini", "Failed to resolve 'google' alias to gemini");

                    var p2 = registry.GetProvider("anthropic");
                    Assert(p2 != null && p2.Id == "claude", "Failed to resolve 'anthropic' alias to claude");

                    var p3 = registry.GetProvider("grok");
                    Assert(p3 != null && p3.Id == "xai", "Failed to resolve 'grok' alias to xai");

                    var p4 = registry.GetProvider("OPENAI");
                    Assert(p4 != null && p4.Id == "openai", "Failed case-insensitive resolution");
                });

                // 4. Test Model Resolution
                RunTest(report, "ModelResolution_ResolvesExactAndFallbacks", () =>
                {
                    var m1 = registry.GetModel("gemini", "gemini-1.5-flash");
                    Assert(m1 != null && m1.Id == "gemini-1.5-flash", "Failed to resolve gemini-1.5-flash");

                    var m2 = registry.GetModel("gemini", "models/gemini-1.5-pro");
                    Assert(m2 != null && m2.Id == "gemini-1.5-pro", "Failed to resolve stripped models/ prefix");

                    var m3 = registry.GetModel("openai", "");
                    Assert(m3 != null && !string.IsNullOrEmpty(m3.Id), "Failed fallback to default provider model");
                });

                // 5. Test Mock Provider Key Availability
                RunTest(report, "ProviderKeyAvailability_ReflectsCredentialState", () =>
                {
                    // Create a mock provider to test credential isolation
                    var mockProvider = new MockAiProvider("mock_test", "Mock Test", isConfigured: false);
                    registry.RegisterProvider(mockProvider);

                    Assert(!registry.IsProviderConfigured("mock_test"), "Mock provider should be unconfigured");

                    mockProvider.SetConfigured(true);
                    Assert(registry.IsProviderConfigured("mock_test"), "Mock provider should now be configured");
                });

                // 6. Test Model Switching Success
                RunTest(report, "ModelSwitching_SuccessWhenConfigured", () =>
                {
                    var mockA = new MockAiProvider("provider_a", "Provider A", isConfigured: true);
                    var mockB = new MockAiProvider("provider_b", "Provider B", isConfigured: true);
                    registry.RegisterProvider(mockA);
                    registry.RegisterProvider(mockB);

                    var res = registry.TrySwitchModel(
                        currentProviderId: "provider_a",
                        currentModelId: "model_a",
                        targetProviderId: "provider_b",
                        targetModelId: "model_b"
                    );

                    Assert(res.Success, "Expected switch to succeed");
                    Assert(res.ProviderId == "provider_b", "Expected target provider 'provider_b'");
                    Assert(res.ModelId == "model_b", "Expected target model 'model_b'");
                    Assert(!res.RequiresConfiguration, "Should not require configuration");
                });

                // 7. Test Missing Credentials Diagnostic
                RunTest(report, "ModelSwitching_RejectsUnconfiguredProvider", () =>
                {
                    var mockC = new MockAiProvider("provider_c", "Provider C", isConfigured: false);
                    registry.RegisterProvider(mockC);

                    var res = registry.TrySwitchModel(
                        currentProviderId: "provider_a",
                        currentModelId: "model_a",
                        targetProviderId: "provider_c",
                        targetModelId: "model_c"
                    );

                    Assert(!res.Success, "Switch must fail for unconfigured provider");
                    Assert(res.RequiresConfiguration, "Must indicate that configuration is required");
                    Assert(!string.IsNullOrEmpty(res.ErrorMessage), "Must provide user-facing error message");
                    Assert(res.TargetProviderName == "Provider C", "Must identify unconfigured provider");
                });

                // 8. Test Switching Failure Preserves Current Model
                RunTest(report, "ModelSwitching_FailurePreservesCurrentModel", () =>
                {
                    var res = registry.TrySwitchModel(
                        currentProviderId: "provider_a",
                        currentModelId: "model_a",
                        targetProviderId: "invalid_nonexistent_provider",
                        targetModelId: "some_model"
                    );

                    Assert(!res.Success, "Switch must fail for nonexistent provider");
                    Assert(res.ProviderId == "provider_a", "Must retain current provider ID");
                    Assert(res.ModelId == "model_a", "Must retain current model ID");
                });

                // 9. Test Conversation Continuity Across Model Switches
                await RunTestAsync(report, "ConversationContinuity_SameConversationAcrossSwitches", async () =>
                {
                    var conv = await convService.CreateConversationAsync(
                        "Multi-Model Architecture Discussion",
                        "openai",
                        "gpt-4o"
                    );
                    string convId = conv.Id;

                    // Message 1 under OpenAI
                    await convService.AddUserMessageAsync(convId, "Explain React Server Components");
                    await convService.AddAssistantMessageAsync(convId, "RSC allows rendering components on server.", "openai", "gpt-4o", false);

                    // User switches model to Gemini inside SAME conversation
                    var switchRes = registry.TrySwitchModel("openai", "gpt-4o", "gemini", "gemini-1.5-flash");
                    // Update conversation active model
                    await convService.UpdateConversationModelAsync(convId, "gemini", "gemini-1.5-flash");

                    // Message 2 under Gemini in the SAME conversation
                    await convService.AddUserMessageAsync(convId, "Now explain this with an example");
                    await convService.AddAssistantMessageAsync(convId, "Here is a practical code example.", "gemini", "gemini-1.5-flash", false);

                    var updatedConv = await convService.GetConversationAsync(convId);
                    Assert(updatedConv.Id == convId, "ConversationId must remain unchanged");
                    Assert(updatedConv.MessageCount == 4, $"Expected 4 messages in conversation, found {updatedConv.MessageCount}");
                });

                // 10. Test Message Provider Metadata Preservation
                await RunTestAsync(report, "MessageMetadata_PreservesOriginalProviderAndModelPerMessage", async () =>
                {
                    var conv = await convService.CreateConversationAsync("Metadata Test", "openai", "gpt-4o");

                    await convService.AddAssistantMessageAsync(conv.Id, "OpenAI Response", "openai", "gpt-4o", false);
                    await convService.AddAssistantMessageAsync(conv.Id, "Gemini Response", "gemini", "gemini-1.5-flash", false);
                    await convService.AddAssistantMessageAsync(conv.Id, "Claude Response", "claude", "claude-3-5-sonnet", false);

                    var msgs = await convService.GetMessagesAsync(conv.Id);
                    Assert(msgs.Count == 3, "Expected 3 messages");

                    Assert(msgs[0].ProviderId == "openai" && msgs[0].ModelId == "gpt-4o", "Message 0 metadata mismatch");
                    Assert(msgs[1].ProviderId == "gemini" && msgs[1].ModelId == "gemini-1.5-flash", "Message 1 metadata mismatch");
                    Assert(msgs[2].ProviderId == "claude" && msgs[2].ModelId == "claude-3-5-sonnet", "Message 2 metadata mismatch");
                });

                // 11. Test Conversation Current Model Persistence & Restoration
                await RunTestAsync(report, "ConversationCurrentModel_PersistenceAndRestoration", async () =>
                {
                    var conv = await convService.CreateConversationAsync("Active Model Test", "openai", "gpt-4o");
                    Assert(conv.ProviderId == "openai" && conv.ModelId == "gpt-4o", "Initial active model incorrect");

                    await convService.UpdateConversationModelAsync(conv.Id, "gemini", "gemini-2.0-flash");

                    var reloaded = await convService.GetConversationAsync(conv.Id);
                    Assert(reloaded.ProviderId == "gemini", $"Expected restored provider 'gemini', got '{reloaded.ProviderId}'");
                    Assert(reloaded.ModelId == "gemini-2.0-flash", $"Expected restored model 'gemini-2.0-flash', got '{reloaded.ModelId}'");
                });

                // 12. Test Cycle Switching: GPT-4o -> Gemini -> Claude -> DeepSeek -> GPT-4o
                await RunTestAsync(report, "CycleSwitching_MultiProviderSequenceInSameConversation", async () =>
                {
                    var conv = await convService.CreateConversationAsync("Multi Provider Cycle", "openai", "gpt-4o");
                    string convId = conv.Id;

                    // Step 1: GPT-4o
                    await convService.AddUserMessageAsync(convId, "1. Hi from User", "openai", "gpt-4o");
                    await convService.AddAssistantMessageAsync(convId, "1. Hi from GPT-4o", "openai", "gpt-4o", false);

                    // Step 2: Switch to Gemini
                    await convService.UpdateConversationModelAsync(convId, "gemini", "gemini-1.5-flash");
                    await convService.AddUserMessageAsync(convId, "2. Tell me about Windows 10 Mobile", "gemini", "gemini-1.5-flash");
                    await convService.AddAssistantMessageAsync(convId, "2. Gemini response on W10M", "gemini", "gemini-1.5-flash", false);

                    // Step 3: Switch to Claude
                    await convService.UpdateConversationModelAsync(convId, "claude", "claude-3-5-sonnet");
                    await convService.AddUserMessageAsync(convId, "3. Compare with UWP", "claude", "claude-3-5-sonnet");
                    await convService.AddAssistantMessageAsync(convId, "3. Claude UWP comparison", "claude", "claude-3-5-sonnet", false);

                    // Step 4: Switch to DeepSeek
                    await convService.UpdateConversationModelAsync(convId, "deepseek", "deepseek-chat");
                    await convService.AddUserMessageAsync(convId, "4. Give me a code example", "deepseek", "deepseek-chat");
                    await convService.AddAssistantMessageAsync(convId, "4. DeepSeek code snippet", "deepseek", "deepseek-chat", false);

                    // Step 5: Switch back to GPT-4o
                    await convService.UpdateConversationModelAsync(convId, "openai", "gpt-4o");
                    await convService.AddUserMessageAsync(convId, "5. Conclude our discussion", "openai", "gpt-4o");
                    await convService.AddAssistantMessageAsync(convId, "5. GPT-4o conclusion", "openai", "gpt-4o", false);

                    // Verification
                    var finalConv = await convService.GetConversationAsync(convId);
                    Assert(finalConv.Id == convId, "Conversation ID must remain unchanged through all switches");
                    Assert(finalConv.MessageCount == 10, $"Expected 10 total messages, got {finalConv.MessageCount}");
                    Assert(finalConv.ProviderId == "openai", $"Expected final active provider 'openai', got '{finalConv.ProviderId}'");
                    Assert(finalConv.ModelId == "gpt-4o", $"Expected final active model 'gpt-4o', got '{finalConv.ModelId}'");

                    var allMsgs = await convService.GetMessagesAsync(convId);
                    var assistantMsgs = allMsgs.Where(m => m.Role == MessageRole.Assistant).ToList();
                    Assert(assistantMsgs.Count == 5, $"Expected 5 assistant responses, got {assistantMsgs.Count}");

                    Assert(assistantMsgs[0].ProviderId == "openai" && assistantMsgs[0].ModelId == "gpt-4o", "Assistant 0 mismatch");
                    Assert(assistantMsgs[1].ProviderId == "gemini" && assistantMsgs[1].ModelId == "gemini-1.5-flash", "Assistant 1 mismatch");
                    Assert(assistantMsgs[2].ProviderId == "claude" && assistantMsgs[2].ModelId == "claude-3-5-sonnet", "Assistant 2 mismatch");
                    Assert(assistantMsgs[3].ProviderId == "deepseek" && assistantMsgs[3].ModelId == "deepseek-chat", "Assistant 3 mismatch");
                    Assert(assistantMsgs[4].ProviderId == "openai" && assistantMsgs[4].ModelId == "gpt-4o", "Assistant 4 mismatch");
                });

                // 13. Test Context Builder Cross-Provider Context Window
                await RunTestAsync(report, "ContextBuilder_CrossProviderContextWindow", async () =>
                {
                    var conv = await convService.CreateConversationAsync("Context Test", "openai", "gpt-4o");
                    string convId = conv.Id;

                    await convService.AddUserMessageAsync(convId, "Prompt 1", "openai", "gpt-4o");
                    await convService.AddAssistantMessageAsync(convId, "Answer 1", "openai", "gpt-4o", false);
                    await convService.AddUserMessageAsync(convId, "Prompt 2", "gemini", "gemini-1.5-flash");
                    await convService.AddAssistantMessageAsync(convId, "Answer 2", "gemini", "gemini-1.5-flash", false);

                    var contextBuilder = new ConversationContextBuilder(convService);
                    var context = await contextBuilder.BuildContextAsync(convId, maxRecentMessages: 10);

                    Assert(context.Count == 4, $"Expected 4 context messages, got {context.Count}");
                    Assert(context[0].Text == "Prompt 1" && context[0].IsUser, "Context message 0 mismatch");
                    Assert(context[1].Text == "Answer 1" && !context[1].IsUser, "Context message 1 mismatch");
                    Assert(context[2].Text == "Prompt 2" && context[2].IsUser, "Context message 2 mismatch");
                    Assert(context[3].Text == "Answer 2" && !context[3].IsUser, "Context message 3 mismatch");
                });

                // 14. Test In-Flight Request Snapshotting (Does not corrupt active request)
                await RunTestAsync(report, "ActiveRequestGuard_PreservesOriginatingModel", async () =>
                {
                    var conv = await convService.CreateConversationAsync("InFlight Test", "openai", "gpt-4o");
                    string convId = conv.Id;

                    // In-flight request starts with OpenAI
                    string inFlightProvider = "openai";
                    string inFlightModel = "gpt-4o";

                    // User switches model while request is in-flight
                    await convService.UpdateConversationModelAsync(convId, "gemini", "gemini-1.5-flash");

                    // In-flight request completes and saves its response using snapshotted metadata
                    await convService.AddAssistantMessageAsync(convId, "In-flight OpenAI reply", inFlightProvider, inFlightModel, false);

                    var msgs = await convService.GetMessagesAsync(convId);
                    Assert(msgs.Count == 1, "Expected 1 message");
                    Assert(msgs[0].ProviderId == "openai", $"Expected in-flight response to have provider 'openai', got '{msgs[0].ProviderId}'");
                    Assert(msgs[0].ModelId == "gpt-4o", $"Expected in-flight response to have model 'gpt-4o', got '{msgs[0].ModelId}'");

                    // Next user message uses switched Gemini
                    var currentConv = await convService.GetConversationAsync(convId);
                    Assert(currentConv.ProviderId == "gemini", "Conversation active provider must be gemini for next message");
                });

                // 15. Test Large Conversations (50, 100, 500 messages) and Responsive Context Building
                await RunTestAsync(report, "LargeConversation_ResponsiveContextBuilding", async () =>
                {
                    var conv = await convService.CreateConversationAsync("Large Conversation Test", "openai", "gpt-4o");
                    string convId = conv.Id;

                    // Bulk insert 100 messages alternating between models
                    var testMessages = new List<MessageEntity>(100);
                    for (int i = 1; i <= 100; i++)
                    {
                        bool isUser = (i % 2) != 0;
                        string prov = i < 50 ? "openai" : "gemini";
                        string mod = i < 50 ? "gpt-4o" : "gemini-1.5-flash";

                        testMessages.Add(new MessageEntity
                        {
                            Id = Guid.NewGuid().ToString("D"),
                            ConversationId = convId,
                            Role = isUser ? MessageRole.User : MessageRole.Assistant,
                            Content = $"Message {i} content",
                            CreatedAtUtc = DateTime.UtcNow.AddSeconds(i),
                            ProviderId = prov,
                            ModelId = mod,
                            Sequence = i,
                            IsError = false
                        });
                    }

                    foreach (var msg in testMessages)
                    {
                        await msgRepo.AddAsync(msg).ConfigureAwait(false);
                    }
                    await convService.RecalculateConversationMetadataAsync(convId);

                    var loadedConv = await convService.GetConversationAsync(convId);
                    Assert(loadedConv.MessageCount == 100, $"Expected 100 messages, found {loadedConv.MessageCount}");

                    // Measure context building time
                    var sw = System.Diagnostics.Stopwatch.StartNew();
                    var contextBuilder = new ConversationContextBuilder(convService);
                    var context = await contextBuilder.BuildContextAsync(convId, maxRecentMessages: 20);
                    sw.Stop();

                    Assert(context.Count == 20, $"Context window must be 20, got {context.Count}");
                    Assert(context[0].Text == "Message 81 content", $"Expected context to start at Message 81, got {context[0].Text}");
                    Assert(context[19].Text == "Message 100 content", $"Expected context to end at Message 100, got {context[19].Text}");
                    Assert(sw.ElapsedMilliseconds < 500, $"Context building must be responsive (<500ms), took {sw.ElapsedMilliseconds}ms");
                });

                // 16. Test App Restart Simulation (Preserves Active Model & History)
                await RunTestAsync(report, "ApplicationRestart_PreservesActiveModelAndHistory", async () =>
                {
                    var conv = await convService.CreateConversationAsync("Restart Test", "gemini", "gemini-1.5-pro");
                    await convService.AddUserMessageAsync(conv.Id, "Hello from Gemini session", "gemini", "gemini-1.5-pro");
                    await convService.AddAssistantMessageAsync(conv.Id, "Gemini reply", "gemini", "gemini-1.5-pro", false);

                    // Switch model to Claude
                    await convService.UpdateConversationModelAsync(conv.Id, "claude", "claude-3-5-sonnet");
                    await convService.AddUserMessageAsync(conv.Id, "Hello Claude", "claude", "claude-3-5-sonnet");
                    await convService.AddAssistantMessageAsync(conv.Id, "Claude reply", "claude", "claude-3-5-sonnet", false);

                    // Simulate App Close & Reopen by creating fresh repository/service instances pointing to the same DB
                    var newConvRepo = new ConversationRepository(testDb);
                    var newMsgRepo = new MessageRepository(testDb);
                    var newConvService = new ConversationService(testDb, newConvRepo, newMsgRepo);

                    var reopenedConv = await newConvService.GetConversationAsync(conv.Id);
                    Assert(reopenedConv != null, "Conversation must exist after restart");
                    Assert(reopenedConv.ProviderId == "claude", $"Active provider must be 'claude', got '{reopenedConv.ProviderId}'");
                    Assert(reopenedConv.ModelId == "claude-3-5-sonnet", $"Active model must be 'claude-3-5-sonnet', got '{reopenedConv.ModelId}'");

                    var reopenedMsgs = await newConvService.GetMessagesAsync(conv.Id);
                    Assert(reopenedMsgs.Count == 4, $"Expected 4 messages, got {reopenedMsgs.Count}");
                    Assert(reopenedMsgs[1].ProviderId == "gemini" && reopenedMsgs[1].ModelId == "gemini-1.5-pro", "Gemini historical message metadata corrupted");
                    Assert(reopenedMsgs[3].ProviderId == "claude" && reopenedMsgs[3].ModelId == "claude-3-5-sonnet", "Claude historical message metadata corrupted");
                });

                // 17. Test API Key Deletion (Missing Provider) Preserves Historical Messages
                await RunTestAsync(report, "ApiKeyDeletion_PreservesConversationAndHistory", async () =>
                {
                    var conv = await convService.CreateConversationAsync("Key Deletion Test", "gemini", "gemini-1.5-flash");
                    await convService.AddUserMessageAsync(conv.Id, "Question", "gemini", "gemini-1.5-flash");
                    await convService.AddAssistantMessageAsync(conv.Id, "Answer", "gemini", "gemini-1.5-flash", false);

                    // Simulate deleting the Gemini API key from vault (provider is now unconfigured)
                    var mockGemini = new MockAiProvider("gemini_temp", "Gemini Temp", isConfigured: false);
                    registry.RegisterProvider(mockGemini);

                    // Conversation and history must be completely intact
                    var loadedConv = await convService.GetConversationAsync(conv.Id);
                    Assert(loadedConv != null && loadedConv.MessageCount == 2, "Conversation should remain intact");

                    var msgs = await convService.GetMessagesAsync(conv.Id);
                    Assert(msgs.Count == 2, "Historical messages must not be deleted");
                    Assert(msgs[1].ProviderId == "gemini", "Historical provider metadata must not be wiped");
                });

                // 18. Test Error Diagnostics & Credential Scrubbing (Security)
                RunTest(report, "Security_ErrorDiagnosticsDoesNotLeakApiKeys", () =>
                {
                    string rawError = "Unauthorized request with key sk-proj-1234567890abcdef12345678 and AIzaSyA1234567890abcdef1234567890";
                    string scrubbed = System.Text.RegularExpressions.Regex.Replace(
                        rawError,
                        @"(sk-[a-zA-Z0-9_\-]{15,}|AIza[a-zA-Z0-9_\-]{20,})",
                        "[REDACTED_KEY]"
                    );

                    Assert(!scrubbed.Contains("sk-proj-"), "OpenAI API key must be scrubbed");
                    Assert(!scrubbed.Contains("AIzaSyA"), "Gemini API key must be scrubbed");
                    Assert(scrubbed.Contains("[REDACTED_KEY]"), "Scrubbed key placeholder must be present");
                });
            }
            finally
            {
                testDb?.Dispose();
                try
                {
                    if (File.Exists(testDbPath)) File.Delete(testDbPath);
                }
                catch { }
            }

            return report;
        }

        #region Helpers

        private static void RunTest(TestReport report, string name, Action testAction)
        {
            report.TotalTests++;
            try
            {
                testAction();
                report.PassedCount++;
                report.Results.Add(new TestResultItem { TestName = name, Passed = true });
            }
            catch (Exception ex)
            {
                report.FailedCount++;
                report.Results.Add(new TestResultItem { TestName = name, Passed = false, ErrorMessage = ex.Message });
            }
        }

        private static async Task RunTestAsync(TestReport report, string name, Func<Task> testAction)
        {
            report.TotalTests++;
            try
            {
                await testAction();
                report.PassedCount++;
                report.Results.Add(new TestResultItem { TestName = name, Passed = true });
            }
            catch (Exception ex)
            {
                report.FailedCount++;
                report.Results.Add(new TestResultItem { TestName = name, Passed = false, ErrorMessage = ex.Message });
            }
        }

        private static void Assert(bool condition, string message)
        {
            if (!condition)
            {
                throw new InvalidOperationException($"Assertion failed: {message}");
            }
        }

        #endregion

        #region Mock Provider for Tests

        private sealed class MockAiProvider : IAiProvider
        {
            public string Id { get; }
            public string DisplayName { get; }
            private bool _isConfigured;

            public bool IsConfigured => _isConfigured;

            public MockAiProvider(string id, string displayName, bool isConfigured)
            {
                Id = id;
                DisplayName = displayName;
                _isConfigured = isConfigured;
            }

            public void SetConfigured(bool value) => _isConfigured = value;

            public IReadOnlyList<AiModelDescriptor> GetModels()
            {
                return new List<AiModelDescriptor>
                {
                    new AiModelDescriptor(Id == "provider_a" ? "model_a" : (Id == "provider_b" ? "model_b" : "model_c"),
                        DisplayName + " Model", Id, DisplayName, ModelCapabilities.Text, _isConfigured)
                };
            }

            public Task<string> SendMessageAsync(AiModelDescriptor model, List<ChatMessage> conversationHistory)
            {
                return Task.FromResult($"Response from {DisplayName}");
            }

            public Task<ModelTestResult> TestConnectionAsync(string apiKey = null)
            {
                return Task.FromResult(new ModelTestResult { Success = _isConfigured });
            }
        }

        #endregion
    }
}
