namespace NServiceBus.AcceptanceTests
{
    using System;
    using System.Collections.Generic;
    using System.Messaging;
    using System.Threading.Tasks;
    using Features;
    using NServiceBus.AcceptanceTesting;
    using NServiceBus.Settings;
    using NUnit.Framework;

    public class When_setting_label_generator : NServiceBusAcceptanceTest
    {
        const string auditQueue = @".\private$\labelAuditQueue";

        [Test]
        public async Task Should_receive_the_message_and_label()
        {
            DeleteAudit();
            try
            {
                await Scenario.Define<Context>(c => { c.Id = Guid.NewGuid(); })
                    .WithEndpoint<Endpoint>(b => b.When((session, c) => session.SendLocal(new MyMessage
                    {
                        Id = c.Id
                    })))
                    .Done(c => c.WasCalled)
                    .Run();
                Assert.AreEqual("MyLabel", ReadMessageLabel());
            }
            finally
            {
                DeleteAudit();
            }
        }

        static void DeleteAudit()
        {
            if (MessageQueue.Exists(auditQueue))
            {
                MessageQueue.Delete(auditQueue);
            }
        }

        static string ReadMessageLabel()
        {
            if (!MessageQueue.Exists(auditQueue))
            {
                return null;
            }
            using (var queue = new MessageQueue(auditQueue))
            using (var message = queue.Receive(TimeSpan.FromSeconds(5)))
            {
                return message?.Label;
            }
        }

        public class Context : ScenarioContext
        {
            public bool WasCalled { get; set; }
            public Guid Id { get; set; }

            public bool GeneratorWasCalled { get; set; }
        }

        class StartHandler : Feature
        {
            protected override void Setup(FeatureConfigurationContext context)
            {
                context.Container.ConfigureComponent<StartHandlerTask>(DependencyLifecycle.SingleInstance);
                context.RegisterStartupTask(b => b.Build<StartHandlerTask>());
            }
        }

        class StartHandlerTask : FeatureStartupTask
        {
            Context context;
            ReadOnlySettings settings;

            public StartHandlerTask(Context context, ReadOnlySettings settings)
            {
                this.context = context;
                this.settings = settings;
            }

            protected override Task OnStart(IMessageSession session)
            {
                context.GeneratorWasCalled = settings.Get<bool>("GeneratorWasCalled");
                return Task.FromResult(0);
            }

            protected override Task OnStop(IMessageSession session)
            {
                return Task.FromResult(0);
            }
        }

        public class Endpoint : EndpointConfigurationBuilder, IWantToRunBeforeConfigurationIsFinalized
        {
            static bool initialized;
            bool generatorWasCalled;

            public Endpoint()
            {
                if (initialized)
                {
                    return;
                }
                initialized = true;
                EndpointSetup<DefaultServer>(c =>
                {
                    c.EnableFeature<StartHandler>();
                    c.AuditProcessedMessagesTo("labelAuditQueue");
                    c.UseTransport<MsmqTransport>().ApplyLabelToMessages(GetMessageLabel);
                });
            }

            static Context Context { get; set; }

            string GetMessageLabel(IReadOnlyDictionary<string, string> headers)
            {
                generatorWasCalled = true;
                return "MyLabel";
            }

            public void Run(SettingsHolder config)
            {
                config.Set("GeneratorWasCalled", generatorWasCalled);
            }
        }

        [Serializable]
        public class MyMessage : ICommand
        {
            public Guid Id { get; set; }
        }

        public class MyMessageHandler : IHandleMessages<MyMessage>
        {
            public Context Context { get; set; }

            public Task Handle(MyMessage message, IMessageHandlerContext context)
            {
                if (Context.Id != message.Id)
                    return Task.FromResult(0);

                Context.WasCalled = true;

                return Task.FromResult(0);
            }
        }
    }


}