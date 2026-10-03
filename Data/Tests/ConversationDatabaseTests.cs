using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using Windows.Storage;
using WinAI.Data.Database;
using WinAI.Data.Models;
using WinAI.Data.Repositories;
using WinAI.Data.Services;

namespace WinAI.Data.Tests
{
    public sealed class TestResultItem
    {
        public string TestName { get; set; }
        public bool Passed { get; set; }
        public string ErrorMessage { get; set; }
    }

    public sealed class TestReport
    {
        public int TotalTests { get; set; }
        public int PassedCount { get; set; }
        public int FailedCount { get; set; }
        public List<TestResultItem> Results { get; } = new List<TestResultItem>();
        public bool AllPassed => FailedCount == 0;
    }

    /// <summary>
    /// Automated test suite validating all local SQLite conversation database operations,
    /// transactions, deterministic ordering, migrations, and cascade deletions.
    /// Runs strictly on isolated temporary storage to protect user data.
    /// </summary>
    public sealed class ConversationDatabaseTests
    {
        public static async Task<TestReport> RunAllTestsAsync(string customTempDir = null)
        {
            var report = new TestReport();
            string tempFolder = customTempDir;
            if (string.IsNullOrEmpty(tempFolder))
            {
                try { tempFolder = ApplicationData.Current.TemporaryFolder.Path; } catch { tempFolder = Path.GetTempPath(); }
            }
            string testDbPath = Path.Combine(tempFolder, $"test_winai_{Guid.NewGuid():N}.db");

            WinAIDatabase testDb = null;
            try
            {
                // 1. Test Database Initialization & Migrations
                await RunTestAsync(report, "Database Initialization & Schema V1", async () =>
                {
                    testDb = await DatabaseInitializer.InitializeAsync(testDbPath).ConfigureAwait(false);
                    int version = await testDb.GetSchemaVersionAsync().ConfigureAwait(false);
                    Assert(version == 1, $"Expected schema version 1, got {version}");
                });

                var convRepo = new ConversationRepository(testDb);
                var msgRepo = new MessageRepository(testDb);
                var convService = new ConversationService(testDb, convRepo, msgRepo);

                string convId = Guid.NewGuid().ToString("D");

                // 2. Test Create Conversation
                await RunTestAsync(report, "Create Conversation", async () =>
                {
                    var entity = new ConversationEntity
                    {
                        Id = convId,
                        Title = "Test Architecture",
                        ProviderId = "openai",
                        ModelId = "gpt-4o"
                    };
                    var created = await convRepo.CreateAsync(entity).ConfigureAwait(false);
                    Assert(created != null && created.Id == convId, "Conversation was not created correctly");
                });

                // 3. Test Retrieve Conversation
                await RunTestAsync(report, "Retrieve Conversation", async () =>
                {
                    var retrieved = await convRepo.GetByIdAsync(convId).ConfigureAwait(false);
                    Assert(retrieved != null, "Failed to retrieve conversation by ID");
                    Assert(retrieved.Title == "Test Architecture", "Retrieved conversation title mismatch");
                });

                // 4. Test Add Message
                await RunTestAsync(report, "Add Message", async () =>
                {
                    var msg = new MessageEntity
                    {
                        ConversationId = convId,
                        Role = MessageRole.User,
                        Content = "Can you explain React Native architecture?"
                    };
                    var added = await msgRepo.AddAsync(msg).ConfigureAwait(false);
                    Assert(added != null && !string.IsNullOrEmpty(added.Id), "Failed to add message");
                    Assert(added.Sequence == 1, $"Expected sequence 1, got {added.Sequence}");
                });

                // 5. Test Retrieve Messages & Deterministic Order
                await RunTestAsync(report, "Retrieve Messages & Deterministic Order", async () =>
                {
                    var msg2 = new MessageEntity
                    {
                        ConversationId = convId,
                        Role = MessageRole.Assistant,
                        Content = "React Native uses a JavaScript bridge or JSI..."
                    };
                    await msgRepo.AddAsync(msg2).ConfigureAwait(false);

                    var messages = await msgRepo.GetByConversationIdAsync(convId).ConfigureAwait(false);
                    Assert(messages.Count == 2, $"Expected 2 messages, found {messages.Count}");
                    Assert(messages[0].Sequence == 1, "First message should have sequence 1");
                    Assert(messages[1].Sequence == 2, "Second message should have sequence 2");
                });

                // 6. Test Update Conversation
                await RunTestAsync(report, "Update Conversation", async () =>
                {
                    var conv = await convRepo.GetByIdAsync(convId).ConfigureAwait(false);
                    conv.Title = "Updated Architecture Topic";
                    bool ok = await convRepo.UpdateAsync(conv).ConfigureAwait(false);
                    Assert(ok, "UpdateAsync returned false");

                    var updated = await convRepo.GetByIdAsync(convId).ConfigureAwait(false);
                    Assert(updated.Title == "Updated Architecture Topic", "Title was not updated");
                });

                // 7. Test Message Count & Last Message Preview via ConversationService
                await RunTestAsync(report, "Message Count & Last Message Preview", async () =>
                {
                    string testConv2 = Guid.NewGuid().ToString("D");
                    await convService.AddUserMessageAsync(testConv2, "What is quantum computing?").ConfigureAwait(false);
                    await convService.AddAssistantMessageAsync(testConv2, "Quantum computing harnesses the phenomena of quantum mechanics.").ConfigureAwait(false);

                    var conv = await convService.GetConversationAsync(testConv2).ConfigureAwait(false);
                    Assert(conv != null, "Conversation not found");
                    Assert(conv.MessageCount == 2, $"Expected message count 2, got {conv.MessageCount}");
                    Assert(conv.LastMessagePreview.Contains("Quantum computing harnesses"), "Preview not updated correctly");
                });

                // 8. Test Automatic Local Title Generation
                await RunTestAsync(report, "Automatic Local Title Generation", async () =>
                {
                    string testConv3 = Guid.NewGuid().ToString("D");
                    await convService.AddUserMessageAsync(testConv3, "Can you explain React Native Architecture?").ConfigureAwait(false);

                    var conv = await convService.GetConversationAsync(testConv3).ConfigureAwait(false);
                    Assert(conv.Title.Contains("React Native Architecture"), $"Expected title to contain topic, got '{conv.Title}'");
                    Assert(!conv.Title.Contains("\n") && !conv.Title.Contains("\r"), "Title must not contain line breaks");
                });

                // 9. Test Pin Conversation
                await RunTestAsync(report, "Pin Conversation", async () =>
                {
                    bool ok = await convService.PinConversationAsync(convId, true).ConfigureAwait(false);
                    Assert(ok, "PinConversationAsync returned false");

                    var conv = await convService.GetConversationAsync(convId).ConfigureAwait(false);
                    Assert(conv.IsPinned, "Conversation was not pinned");

                    var pinnedList = await convService.GetPinnedConversationsAsync().ConfigureAwait(false);
                    Assert(pinnedList.Exists(c => c.Id == convId), "Conversation missing from pinned list");
                });

                // 10. Test Archive Conversation
                await RunTestAsync(report, "Archive Conversation", async () =>
                {
                    bool ok = await convService.ArchiveConversationAsync(convId, true).ConfigureAwait(false);
                    Assert(ok, "ArchiveConversationAsync returned false");

                    var activeList = await convService.GetConversationsAsync(includeArchived: false).ConfigureAwait(false);
                    Assert(!activeList.Exists(c => c.Id == convId), "Archived conversation should not appear in active list");

                    var allList = await convService.GetConversationsAsync(includeArchived: true).ConfigureAwait(false);
                    Assert(allList.Exists(c => c.Id == convId), "Archived conversation missing when includeArchived = true");
                });

                // 11. Test Search
                await RunTestAsync(report, "Search Conversations & Messages", async () =>
                {
                    var results = await convService.SearchConversationsAsync("Quantum").ConfigureAwait(false);
                    Assert(results.Count > 0, "Search for 'Quantum' should find matching conversation");
                });

                // 12. Test Transaction Rollback
                await RunTestAsync(report, "Transaction Rollback on Error", async () =>
                {
                    int initialCount = await convRepo.GetCountAsync(true).ConfigureAwait(false);
                    try
                    {
                        await testDb.RunInTransactionAsync(async db =>
                        {
                            db.ExecuteNonQueryInternal(@"
                                INSERT INTO Conversations (Id, Title, CreatedAtUtc, UpdatedAtUtc)
                                VALUES ('temp_tx_id', 'Should Roll Back', '2026-01-01', '2026-01-01');
                            ");
                            await Task.CompletedTask;
                            // Force an intentional exception to trigger rollback
                            throw new InvalidOperationException("Forced transaction rollback test");
                        }).ConfigureAwait(false);
                    }
                    catch (InvalidOperationException)
                    {
                        // Expected exception
                    }

                    int afterCount = await convRepo.GetCountAsync(true).ConfigureAwait(false);
                    Assert(initialCount == afterCount, "Transaction was not rolled back; row count changed");
                });

                // 13. Test Delete Messages
                await RunTestAsync(report, "Delete Messages for Conversation", async () =>
                {
                    int deleted = await msgRepo.DeleteByConversationIdAsync(convId).ConfigureAwait(false);
                    Assert(deleted >= 0, "DeleteByConversationIdAsync failed");

                    var msgs = await msgRepo.GetByConversationIdAsync(convId).ConfigureAwait(false);
                    Assert(msgs.Count == 0, "Messages still exist after deletion");
                });

                // 14. Test Delete Conversation (Referential Cleanup)
                await RunTestAsync(report, "Delete Conversation Cascade", async () =>
                {
                    string toDeleteId = Guid.NewGuid().ToString("D");
                    await convService.AddUserMessageAsync(toDeleteId, "Temporary message to delete").ConfigureAwait(false);

                    bool deleted = await convService.DeleteConversationAsync(toDeleteId).ConfigureAwait(false);
                    Assert(deleted, "DeleteConversationAsync returned false");

                    var conv = await convService.GetConversationAsync(toDeleteId).ConfigureAwait(false);
                    Assert(conv == null, "Conversation still exists after deletion");

                    var msgs = await msgRepo.GetByConversationIdAsync(toDeleteId).ConfigureAwait(false);
                    Assert(msgs.Count == 0, "Orphaned messages found after conversation deletion");
                });

                // 15. Test Migration Behavior (Re-running migrations is idempotent)
                await RunTestAsync(report, "Migration Idempotency", async () =>
                {
                    await DatabaseMigrations.ApplyMigrationsAsync(testDb).ConfigureAwait(false);
                    int version = await testDb.GetSchemaVersionAsync().ConfigureAwait(false);
                    Assert(version == 1, $"Schema version should remain 1, got {version}");
                });

                // 16. Phase 2 Test: Context Builder - Windowing and Error Exclusion
                await RunTestAsync(report, "Phase 2: Context Builder Windowing & Error Exclusion", async () =>
                {
                    string contextConvId = Guid.NewGuid().ToString("D");
                    var contextBuilder = new ConversationContextBuilder(convService);

                    // Add 15 user and assistant messages
                    for (int i = 1; i <= 15; i++)
                    {
                        if (i % 2 == 1)
                        {
                            await convService.AddUserMessageAsync(contextConvId, $"User message {i}").ConfigureAwait(false);
                        }
                        else
                        {
                            await convService.AddAssistantMessageAsync(contextConvId, $"Assistant message {i}").ConfigureAwait(false);
                        }
                    }

                    // Add an error message
                    await convService.AddAssistantMessageAsync(contextConvId, "Error connecting to provider", isError: true).ConfigureAwait(false);

                    // Request context with window size of 6
                    var context = await contextBuilder.BuildContextAsync(contextConvId, maxRecentMessages: 6).ConfigureAwait(false);

                    Assert(context != null, "Context should not be null");
                    Assert(context.Count == 6, $"Expected exactly 6 context messages, got {context.Count}");
                    Assert(!context.Exists(m => m.Text.Contains("Error connecting")), "Error message must be excluded from context");

                    // Verify messages are strictly ordered ascending (10, 11, 12, 13, 14, 15)
                    Assert(context[0].Text == "User message 10" || context[0].Text == "Assistant message 10", "Oldest message in window should be message 10");
                    Assert(context[5].Text == "User message 15", "Latest message in window should be message 15");
                });

                // 17. Phase 2 Test: User Message Persistence on AI Provider Failure
                await RunTestAsync(report, "Phase 2: User Message Preserved on Failure", async () =>
                {
                    string failConvId = Guid.NewGuid().ToString("D");
                    var userMsg = await convService.AddUserMessageAsync(failConvId, "Crucial prompt before crash").ConfigureAwait(false);

                    Assert(userMsg != null, "User message entity should be created");

                    // Simulate simulated network failure: No assistant message is added
                    // Check database directly:
                    var retrievedConv = await convService.GetConversationAsync(failConvId).ConfigureAwait(false);
                    var retrievedMsgs = await convService.GetMessagesAsync(failConvId).ConfigureAwait(false);

                    Assert(retrievedConv != null, "Conversation should exist in SQLite");
                    Assert(retrievedMsgs.Count == 1, $"Expected 1 message, found {retrievedMsgs.Count}");
                    Assert(retrievedMsgs[0].Content == "Crucial prompt before crash", "User message content must be preserved");
                    Assert(retrievedMsgs[0].Role == MessageRole.User, "Message role must be User");
                });

                // 18. Phase 2 Test: Save and Continue Conversation
                await RunTestAsync(report, "Phase 2: Save and Continue Conversation", async () =>
                {
                    string continueConvId = Guid.NewGuid().ToString("D");
                    await convService.AddUserMessageAsync(continueConvId, "First message").ConfigureAwait(false);
                    await convService.AddAssistantMessageAsync(continueConvId, "First reply").ConfigureAwait(false);

                    // Continue conversation with new message
                    await convService.AddUserMessageAsync(continueConvId, "Second message").ConfigureAwait(false);
                    await convService.AddAssistantMessageAsync(continueConvId, "Second reply").ConfigureAwait(false);

                    var allMsgs = await convService.GetMessagesAsync(continueConvId).ConfigureAwait(false);
                    Assert(allMsgs.Count == 4, $"Expected 4 messages in continued conversation, got {allMsgs.Count}");
                    Assert(allMsgs[0].Sequence == 1, "Msg 1 sequence must be 1");
                    Assert(allMsgs[1].Sequence == 2, "Msg 2 sequence must be 2");
                    Assert(allMsgs[2].Sequence == 3, "Msg 3 sequence must be 3");
                    Assert(allMsgs[3].Sequence == 4, "Msg 4 sequence must be 4");

                    var conv = await convService.GetConversationAsync(continueConvId).ConfigureAwait(false);
                    Assert(conv.MessageCount == 4, $"Conversation message count should be 4, got {conv.MessageCount}");
                });

                // 19. Phase 2 Test: Provider and Model Metadata Persistence
                await RunTestAsync(report, "Phase 2: Provider and Model Switching Persistence", async () =>
                {
                    string switchConvId = Guid.NewGuid().ToString("D");
                    var conv = await convService.CreateConversationAsync("Switching Test", "openai", "gpt-4o", switchConvId).ConfigureAwait(false);
                    Assert(conv.ProviderId == "openai", "Initial provider must be openai");
                    Assert(conv.ModelId == "gpt-4o", "Initial model must be gpt-4o");

                    // User switches to Claude 3.5 Sonnet
                    bool updated = await convService.UpdateConversationModelAsync(switchConvId, "claude", "claude-3-5-sonnet").ConfigureAwait(false);
                    Assert(updated, "UpdateConversationModelAsync should return true");

                    var reloaded = await convService.GetConversationAsync(switchConvId).ConfigureAwait(false);
                    Assert(reloaded.ProviderId == "claude", "Updated provider must be claude");
                    Assert(reloaded.ModelId == "claude-3-5-sonnet", "Updated model must be claude-3-5-sonnet");
                });

                // 20. Phase 3 Test: Date Grouping Categorization (Today, Yesterday, This Week, Older)
                await RunTestAsync(report, "Phase 3: Date Grouping Categorization", async () =>
                {
                    DateTime today = DateTime.Today;
                    DateTime yesterday = today.AddDays(-1);
                    DateTime fourDaysAgo = today.AddDays(-4);
                    DateTime twoWeeksAgo = today.AddDays(-14);

                    var cToday = new ConversationEntity { Id = "grp_today", Title = "Today Conv", CreatedAtUtc = today.ToUniversalTime(), UpdatedAtUtc = today.ToUniversalTime() };
                    var cYesterday = new ConversationEntity { Id = "grp_yest", Title = "Yesterday Conv", CreatedAtUtc = yesterday.ToUniversalTime(), UpdatedAtUtc = yesterday.ToUniversalTime() };
                    var cWeek = new ConversationEntity { Id = "grp_week", Title = "Week Conv", CreatedAtUtc = fourDaysAgo.ToUniversalTime(), UpdatedAtUtc = fourDaysAgo.ToUniversalTime() };
                    var cOlder = new ConversationEntity { Id = "grp_older", Title = "Older Conv", CreatedAtUtc = twoWeeksAgo.ToUniversalTime(), UpdatedAtUtc = twoWeeksAgo.ToUniversalTime() };

                    await convRepo.CreateAsync(cToday).ConfigureAwait(false);
                    await convRepo.CreateAsync(cYesterday).ConfigureAwait(false);
                    await convRepo.CreateAsync(cWeek).ConfigureAwait(false);
                    await convRepo.CreateAsync(cOlder).ConfigureAwait(false);

                    // Verify date categorization in local timezone
                    Assert(cToday.LocalUpdatedAt.Date == today, "Today conversation must match local today");
                    Assert(cYesterday.LocalUpdatedAt.Date == yesterday, "Yesterday conversation must match local yesterday");
                    Assert(cWeek.LocalUpdatedAt.Date >= today.AddDays(-7) && cWeek.LocalUpdatedAt.Date < yesterday, "Week conversation must fall within past 7 days");
                    Assert(cOlder.LocalUpdatedAt.Date < today.AddDays(-7), "Older conversation must be older than 7 days");
                });

                // 21. Phase 3 Test: Search Title and LastMessagePreview
                await RunTestAsync(report, "Phase 3: Search Title and LastMessagePreview", async () =>
                {
                    string searchConvId = Guid.NewGuid().ToString("D");
                    var conv = await convService.CreateConversationAsync("Special Query Topic", "openai", "gpt-4o", searchConvId).ConfigureAwait(false);
                    await convService.AddUserMessageAsync(searchConvId, "Tell me about quantum teleportation phenomena").ConfigureAwait(false);

                    // Search by title keyword
                    var titleResults = await convService.SearchConversationsAsync("Special Query").ConfigureAwait(false);
                    Assert(titleResults.Exists(c => c.Id == searchConvId), "Should find conversation by Title keyword");

                    // Search by message content / preview keyword
                    var previewResults = await convService.SearchConversationsAsync("teleportation").ConfigureAwait(false);
                    Assert(previewResults.Exists(c => c.Id == searchConvId), "Should find conversation by LastMessagePreview keyword");
                });

                // 22. Phase 3 Test: Pinned Filter and Dynamic Pin Toggling
                await RunTestAsync(report, "Phase 3: Pinned Filter and Dynamic Pin Toggling", async () =>
                {
                    string pinConvId = Guid.NewGuid().ToString("D");
                    await convService.CreateConversationAsync("Pin Test", "openai", "gpt-4o", pinConvId).ConfigureAwait(false);

                    // Initially unpinned
                    var unpinned = await convService.GetPinnedConversationsAsync().ConfigureAwait(false);
                    Assert(!unpinned.Exists(c => c.Id == pinConvId), "Should not be pinned initially");

                    // Pin conversation
                    await convService.PinConversationAsync(pinConvId, true).ConfigureAwait(false);
                    var pinned = await convService.GetPinnedConversationsAsync().ConfigureAwait(false);
                    Assert(pinned.Exists(c => c.Id == pinConvId), "Should be in pinned list after PinConversationAsync(true)");

                    // Unpin conversation
                    await convService.PinConversationAsync(pinConvId, false).ConfigureAwait(false);
                    var reUnpinned = await convService.GetPinnedConversationsAsync().ConfigureAwait(false);
                    Assert(!reUnpinned.Exists(c => c.Id == pinConvId), "Should not be in pinned list after unpinning");
                });

                // 23. Phase 3 Test: Conversation Rename and Immediate Persistence
                await RunTestAsync(report, "Phase 3: Conversation Rename Persistence", async () =>
                {
                    string renameConvId = Guid.NewGuid().ToString("D");
                    await convService.CreateConversationAsync("Old Title", "openai", "gpt-4o", renameConvId).ConfigureAwait(false);

                    bool renamed = await convService.UpdateConversationTitleAsync(renameConvId, "New Brand Title").ConfigureAwait(false);
                    Assert(renamed, "UpdateConversationTitleAsync returned false");

                    var reloaded = await convService.GetConversationAsync(renameConvId).ConfigureAwait(false);
                    Assert(reloaded != null && reloaded.Title == "New Brand Title", "Title did not update in SQLite");
                });

                // 24. Phase 3 Test: MessageCount Accuracy without Full Message Loading
                await RunTestAsync(report, "Phase 3: MessageCount Metadata Accuracy", async () =>
                {
                    string countConvId = Guid.NewGuid().ToString("D");
                    await convService.AddUserMessageAsync(countConvId, "Message 1").ConfigureAwait(false);
                    await convService.AddAssistantMessageAsync(countConvId, "Reply 1").ConfigureAwait(false);
                    await convService.AddUserMessageAsync(countConvId, "Message 2").ConfigureAwait(false);

                    // Fetch metadata ONLY via GetConversationAsync
                    var convMeta = await convService.GetConversationAsync(countConvId).ConfigureAwait(false);
                    Assert(convMeta != null, "Conversation should exist");
                    Assert(convMeta.MessageCount == 3, $"Expected persisted MessageCount 3, got {convMeta.MessageCount}");

                    // Verify LastMessagePreview contains latest message snippet
                    Assert(convMeta.LastMessagePreview.Contains("Message 2"), $"Expected preview to contain latest message, got '{convMeta.LastMessagePreview}'");
                });

                // 25. Phase 4 Test: Transaction Reentrancy & Nested Transaction
                await RunTestAsync(report, "Phase 4: Transaction Reentrancy & Nested Transaction", async () =>
                {
                    string reentrantConvId = Guid.NewGuid().ToString("D");
                    await testDb.RunInTransactionAsync(async db =>
                    {
                        // Direct repository call inside transaction (would deadlock with non-reentrant locks)
                        var entity = new ConversationEntity
                        {
                            Id = reentrantConvId,
                            Title = "Reentrancy Master",
                            CreatedAtUtc = DateTime.UtcNow,
                            UpdatedAtUtc = DateTime.UtcNow
                        };
                        await convRepo.CreateAsync(entity).ConfigureAwait(false);

                        // Nested transaction using SAVEPOINT
                        await db.RunInTransactionAsync(async nestedDb =>
                        {
                            var msg = new MessageEntity
                            {
                                ConversationId = reentrantConvId,
                                Role = MessageRole.User,
                                Content = "Nested transaction message"
                            };
                            await msgRepo.AddAsync(msg).ConfigureAwait(false);
                        }).ConfigureAwait(false);
                    }).ConfigureAwait(false);

                    var loadedConv = await convRepo.GetByIdAsync(reentrantConvId).ConfigureAwait(false);
                    Assert(loadedConv != null && loadedConv.Title == "Reentrancy Master", "Reentrant transaction failed to commit conversation");

                    var msgs = await msgRepo.GetByConversationIdAsync(reentrantConvId).ConfigureAwait(false);
                    Assert(msgs.Count == 1, "Nested transaction message was not committed");
                });

                // 26. Phase 4 Test: Concurrent Asynchronous Execution
                await RunTestAsync(report, "Phase 4: Concurrent Async DB Operations", async () =>
                {
                    var tasks = new List<Task>();
                    for (int i = 0; i < 10; i++)
                    {
                        int index = i;
                        tasks.Add(Task.Run(async () =>
                        {
                            string cId = $"concurrent_{index}_{Guid.NewGuid():N}";
                            await convRepo.CreateAsync(new ConversationEntity
                            {
                                Id = cId,
                                Title = $"Concurrent Chat {index}",
                                CreatedAtUtc = DateTime.UtcNow,
                                UpdatedAtUtc = DateTime.UtcNow
                            }).ConfigureAwait(false);

                            var fetched = await convRepo.GetByIdAsync(cId).ConfigureAwait(false);
                            Assert(fetched != null, $"Concurrent fetch failed for index {index}");
                        }));
                    }
                    await Task.WhenAll(tasks).ConfigureAwait(false);
                });

                // 27. Phase 4 Test: Large Conversation Incremental Loading & Windowing
                await RunTestAsync(report, "Phase 4: Large Conversation Incremental Windowing (120 Messages)", async () =>
                {
                    string largeConvId = Guid.NewGuid().ToString("D");
                    await convRepo.CreateAsync(new ConversationEntity
                    {
                        Id = largeConvId,
                        Title = "Large Conversation Test",
                        CreatedAtUtc = DateTime.UtcNow,
                        UpdatedAtUtc = DateTime.UtcNow
                    }).ConfigureAwait(false);

                    // Insert 120 sequential messages in a single transaction
                    await testDb.RunInTransactionAsync(async db =>
                    {
                        for (int i = 1; i <= 120; i++)
                        {
                            var msg = new MessageEntity
                            {
                                Id = Guid.NewGuid().ToString("D"),
                                ConversationId = largeConvId,
                                Role = (i % 2 == 1) ? MessageRole.User : MessageRole.Assistant,
                                Content = $"Message payload sequence #{i}",
                                Sequence = i,
                                CreatedAtUtc = DateTime.UtcNow.AddMinutes(i)
                            };
                            await msgRepo.AddAsync(msg).ConfigureAwait(false);
                        }
                    }).ConfigureAwait(false);

                    // Fetch the latest 40 messages
                    var recent40 = await convService.GetRecentMessagesAsync(largeConvId, 40).ConfigureAwait(false);
                    Assert(recent40.Count == 40, $"Expected 40 recent messages, got {recent40.Count}");
                    Assert(recent40[0].Sequence == 81, $"First item should be sequence 81, got {recent40[0].Sequence}");
                    Assert(recent40[39].Sequence == 120, $"Last item should be sequence 120, got {recent40[39].Sequence}");

                    // Fetch previous 40 messages before sequence 81
                    var older40 = await convService.GetMessagesBeforeSequenceAsync(largeConvId, 81, 40).ConfigureAwait(false);
                    Assert(older40.Count == 40, $"Expected 40 older messages, got {older40.Count}");
                    Assert(older40[0].Sequence == 41, $"First item of older chunk should be sequence 41, got {older40[0].Sequence}");
                    Assert(older40[39].Sequence == 80, $"Last item of older chunk should be sequence 80, got {older40[39].Sequence}");
                });

                // 28. Phase 4 Test: Large Dataset Performance Benchmark (< 250ms)
                await RunTestAsync(report, "Phase 4: Large Dataset Benchmark (100 Conversations)", async () =>
                {
                    // Batch insert 100 conversations
                    await testDb.RunInTransactionAsync(async db =>
                    {
                        for (int i = 1; i <= 100; i++)
                        {
                            var entity = new ConversationEntity
                            {
                                Id = $"bench_{i}_{Guid.NewGuid():N}",
                                Title = (i == 42) ? "Target Benchmark Conversation Title" : $"Benchmark Conversation #{i}",
                                CreatedAtUtc = DateTime.UtcNow.AddHours(-i),
                                UpdatedAtUtc = DateTime.UtcNow.AddHours(-i),
                                MessageCount = 5,
                                LastMessagePreview = $"Preview snippet for benchmark item {i}"
                            };
                            await convRepo.CreateAsync(entity).ConfigureAwait(false);
                        }
                    }).ConfigureAwait(false);

                    // Benchmark GetAllActive
                    var sw = System.Diagnostics.Stopwatch.StartNew();
                    var allActive = await convService.GetConversationsAsync(false).ConfigureAwait(false);
                    sw.Stop();
                    Assert(allActive.Count >= 100, $"Expected at least 100 conversations, got {allActive.Count}");
                    Assert(sw.ElapsedMilliseconds < 500, $"Load 100 conversations too slow: {sw.ElapsedMilliseconds}ms (limit: 500ms)");

                    // Benchmark Search
                    sw.Restart();
                    var searchHits = await convService.SearchConversationsAsync("Target Benchmark").ConfigureAwait(false);
                    sw.Stop();
                    Assert(searchHits.Count >= 1, "Failed to find target benchmark conversation");
                    Assert(sw.ElapsedMilliseconds < 250, $"Search benchmark too slow: {sw.ElapsedMilliseconds}ms (limit: 250ms)");
                });

                // 29. Phase 4 Test: Metadata Synchronization & Recalculation
                await RunTestAsync(report, "Phase 4: Metadata Recalculation & Sync", async () =>
                {
                    string syncConvId = Guid.NewGuid().ToString("D");
                    await convService.CreateConversationAsync("Sync Chat", "openai", "gpt-4o", syncConvId).ConfigureAwait(false);
                    var m1 = await convService.AddUserMessageAsync(syncConvId, "Initial message").ConfigureAwait(false);
                    var m2 = await convService.AddAssistantMessageAsync(syncConvId, "Initial response").ConfigureAwait(false);

                    // Delete second message
                    await msgRepo.DeleteAsync(m2.Id).ConfigureAwait(false);

                    // Recalculate metadata
                    bool synced = await convService.RecalculateConversationMetadataAsync(syncConvId).ConfigureAwait(false);
                    Assert(synced, "RecalculateConversationMetadataAsync returned false");

                    var conv = await convService.GetConversationAsync(syncConvId).ConfigureAwait(false);
                    Assert(conv.MessageCount == 1, $"MessageCount should be 1 after recalculation, got {conv.MessageCount}");
                    Assert(conv.LastMessagePreview.Contains("Initial message"), $"LastMessagePreview should be 'Initial message', got '{conv.LastMessagePreview}'");
                });

                // 30. Phase 4 Test: Privacy & Zero Plaintext API Key Separation
                await RunTestAsync(report, "Phase 4: Privacy & API Key Separation Audit", async () =>
                {
                    // Inspect schema for Conversations table
                    var convCols = await testDb.QueryAsync("PRAGMA table_info(Conversations);", row => row.GetString("name")).ConfigureAwait(false);
                    foreach (var col in convCols)
                    {
                        string lower = col.ToLowerInvariant();
                        Assert(!lower.Contains("key") && !lower.Contains("secret") && !lower.Contains("password") && !lower.Contains("bearer") && !lower.Contains("apikey") && !lower.Contains("authtoken") && lower != "token",
                            $"Forbidden credential column '{col}' found in Conversations table. API keys must never enter database schema.");
                    }

                    // Inspect schema for Messages table
                    var msgCols = await testDb.QueryAsync("PRAGMA table_info(Messages);", row => row.GetString("name")).ConfigureAwait(false);
                    foreach (var col in msgCols)
                    {
                        string lower = col.ToLowerInvariant();
                        Assert(!lower.Contains("key") && !lower.Contains("secret") && !lower.Contains("password") && !lower.Contains("bearer") && !lower.Contains("apikey") && !lower.Contains("authtoken") && lower != "token",
                            $"Forbidden credential column '{col}' found in Messages table. API keys must never enter database schema.");
                    }
                });

                // 31. Phase 4 Test: Cascade Deletion & Referential Integrity
                await RunTestAsync(report, "Phase 4: Cascade Deletion Referential Integrity", async () =>
                {
                    string cascadeId = Guid.NewGuid().ToString("D");
                    await convRepo.CreateAsync(new ConversationEntity
                    {
                        Id = cascadeId,
                        Title = "Cascade Test",
                        CreatedAtUtc = DateTime.UtcNow,
                        UpdatedAtUtc = DateTime.UtcNow
                    }).ConfigureAwait(false);

                    for (int i = 0; i < 5; i++)
                    {
                        await msgRepo.AddAsync(new MessageEntity
                        {
                            ConversationId = cascadeId,
                            Role = MessageRole.User,
                            Content = $"Message {i}"
                        }).ConfigureAwait(false);
                    }

                    int countBefore = await msgRepo.GetCountForConversationAsync(cascadeId).ConfigureAwait(false);
                    Assert(countBefore == 5, $"Expected 5 messages before deletion, got {countBefore}");

                    bool deleted = await convRepo.DeleteAsync(cascadeId).ConfigureAwait(false);
                    Assert(deleted, "DeleteAsync returned false");

                    int countAfter = await msgRepo.GetCountForConversationAsync(cascadeId).ConfigureAwait(false);
                    Assert(countAfter == 0, $"Expected 0 messages after cascade deletion, got {countAfter}");
                });

                // 32. Phase 4 Test: Corruption Auto-Detection & Recovery
                await RunTestAsync(report, "Phase 4: Corruption Auto-Detection & Recovery", async () =>
                {
                    string corruptDbPath = Path.Combine(tempFolder, $"corrupt_test_{Guid.NewGuid():N}.db");
                    try
                    {
                        // Write completely corrupt byte stream (random non-SQLite data)
                        byte[] corruptBytes = Encoding.UTF8.GetBytes("THIS_IS_NOT_A_VALID_SQLITE_DATABASE_HEADER_CORRUPTED_STREAM");
                        File.WriteAllBytes(corruptDbPath, corruptBytes);

                        // Instantiate WinAIDatabase pointing to the corrupt file
                        using (var corruptDb = new WinAIDatabase(corruptDbPath))
                        {
                            // OpenAsync must detect corruption, rotate file to .corrupt_*, and create a clean database
                            await corruptDb.OpenAsync().ConfigureAwait(false);
                            bool isHealthy = await corruptDb.CheckIntegrityAsync().ConfigureAwait(false);
                            Assert(isHealthy, "Database failed to recover from corruption cleanly");
                        }
                    }
                    finally
                    {
                        try
                        {
                            if (File.Exists(corruptDbPath)) File.Delete(corruptDbPath);
                            // Clean up any .corrupt_* backup files
                            foreach (var f in Directory.GetFiles(tempFolder, Path.GetFileName(corruptDbPath) + "*"))
                            {
                                try { File.Delete(f); } catch { }
                            }
                        }
                        catch { }
                    }
                });
            }
            finally
            {
                if (testDb != null)
                {
                    testDb.Dispose();
                }

                // Clean up temporary database files
                try
                {
                    if (File.Exists(testDbPath)) File.Delete(testDbPath);
                    if (File.Exists(testDbPath + "-wal")) File.Delete(testDbPath + "-wal");
                    if (File.Exists(testDbPath + "-shm")) File.Delete(testDbPath + "-shm");
                }
                catch { }
            }

            return report;
        }

        private static async Task RunTestAsync(TestReport report, string name, Func<Task> testAction)
        {
            report.TotalTests++;
            var item = new TestResultItem { TestName = name };
            try
            {
                await testAction().ConfigureAwait(false);
                item.Passed = true;
                report.PassedCount++;
            }
            catch (Exception ex)
            {
                item.Passed = false;
                item.ErrorMessage = ex.Message;
                report.FailedCount++;
            }
            report.Results.Add(item);
        }

        private static void Assert(bool condition, string message)
        {
            if (!condition)
            {
                throw new InvalidOperationException($"Assertion Failed: {message}");
            }
        }
    }
}
