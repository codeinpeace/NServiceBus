﻿namespace NServiceBus.AcceptanceTests.CriticalError
{
    using System;
    using System.Collections.Concurrent;
    using System.Threading.Tasks;
    using Features;
    using NServiceBus.AcceptanceTesting;
    using NServiceBus.AcceptanceTests.EndpointTemplates;
    using NUnit.Framework;
    using CriticalError = NServiceBus.CriticalError;

    public class When_raising_critical_error : NServiceBusAcceptanceTest
    {
        [Test]
        public async Task Should_trigger_critical_error_action_when_raised_from_handler()
        {
            var exceptions = new ConcurrentDictionary<string, Exception>();

            Func<ICriticalErrorContext, Task> addCritical = criticalContext =>
            {
                exceptions.TryAdd(criticalContext.Error, criticalContext.Exception);
                return Task.FromResult(0);
            };

            await Scenario.Define<TestContext>()
                .WithEndpoint<EndpointWithCriticalError>(b =>
                {
                    b.CustomConfig(config =>
                    {
                        config.DefineCriticalErrorAction(addCritical);
                    });

                    b.When((session, c) =>
                    {
                        c.ContextId = Guid.NewGuid().ToString();
                        return session.SendLocal(new Message
                        {
                            ContextId = c.ContextId
                        });
                    });
                })
                .Done(c => c.CriticalErrorsRaised > 0)
                .Run();

            Assert.AreEqual(1, exceptions.Keys.Count);
        }

        [Test]
        public async Task Should_call_critical_error_action_for_every_error_that_occurred_before_startup()
        {
            var exceptions = new ConcurrentDictionary<string, Exception>();

            Func<ICriticalErrorContext, Task> addCritical = criticalContext =>
            {
                exceptions.TryAdd(criticalContext.Error, criticalContext.Exception);
                return Task.FromResult(0);
            };

            var context = await Scenario.Define<TestContext>()
                .WithEndpoint<EndpointWithCriticalErrorStartup>(b =>
                {
                    b.CustomConfig(config =>
                    {
                        config.DefineCriticalErrorAction(addCritical);
                    });
                })
                .Done(c => c.EndpointsStarted)
                .Run();

            Assert.AreEqual(2, context.CriticalErrorsRaised);
            Assert.AreEqual(exceptions.Keys.Count, context.CriticalErrorsRaised);
        }

        public class TestContext : ScenarioContext
        {
            public string ContextId { get; set; }
            public int CriticalErrorsRaised { get; set; }
        }

        public class EndpointWithCriticalError : EndpointConfigurationBuilder
        {
            public EndpointWithCriticalError()
            {
                EndpointSetup<DefaultServer>();
            }

            public class CriticalHandler : IHandleMessages<Message>
            {
                CriticalError criticalError;
                TestContext testContext;

                public CriticalHandler(CriticalError criticalError, TestContext testContext)
                {
                    this.criticalError = criticalError;
                    this.testContext = testContext;
                }

                public Task Handle(Message request, IMessageHandlerContext context)
                {
                    if (testContext.ContextId == request.ContextId)
                    {
                        criticalError.Raise("a critical error", new SimulatedException());
                        testContext.CriticalErrorsRaised++;
                    }

                    return Task.FromResult(0);
                }
            }
        }

        class CriticalErrorStartup : Feature
        {
            protected override void Setup(FeatureConfigurationContext context)
            {
                context.Container.ConfigureComponent<CriticalErrorStartupFeatureTask>(DependencyLifecycle.SingleInstance);  
                context.RegisterStartupTask(b => b.Build<CriticalErrorStartupFeatureTask>());
            }

            class CriticalErrorStartupFeatureTask : FeatureStartupTask
            {
                readonly CriticalError criticalError;
                readonly TestContext testContext;

                public CriticalErrorStartupFeatureTask(CriticalError criticalError, TestContext testContext)
                {
                    this.criticalError = criticalError;
                    this.testContext = testContext;
                }

                protected override Task OnStart(IMessageSession session)
                {
                    criticalError.Raise("critical error 1", new SimulatedException());
                    testContext.CriticalErrorsRaised++;

                    criticalError.Raise("critical error 2", new SimulatedException());
                    testContext.CriticalErrorsRaised++;

                    return Task.FromResult(0);
                }

                protected override Task OnStop(IMessageSession session)
                {
                    return Task.FromResult(0);
                }
            }
        }

        public class EndpointWithCriticalErrorStartup : EndpointConfigurationBuilder
        {
            public EndpointWithCriticalErrorStartup()
            {
                EndpointSetup<DefaultServer>(c => c.EnableFeature<CriticalErrorStartup>());
            }
        }

        [Serializable]
        public class Message : IMessage
        {
            public string ContextId { get; set; }
        }
    }
}