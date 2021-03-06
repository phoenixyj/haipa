﻿using System;
using System.Threading.Tasks;
using Haipa.Messages.Commands.OperationTasks;
using Haipa.Messages.Operations;
using JetBrains.Annotations;
using Rebus.Bus;
using Rebus.Handlers;
using Rebus.Sagas;

namespace Haipa.Modules.Controller.Operations.Workflows
{
    [UsedImplicitly]
    internal class CreateOrUpdateMachineSaga : OperationTaskWorkflowSaga<CreateOrUpdateMachineCommand, CreateOrUpdateMachineSagaData>,
        IHandleMessages<OperationTaskStatusEvent<PlaceVirtualMachineCommand>>,
        IHandleMessages<OperationTaskStatusEvent<ConvergeVirtualMachineCommand>>
    {
        private readonly IOperationTaskDispatcher _taskDispatcher;

        public CreateOrUpdateMachineSaga(IBus bus, IOperationTaskDispatcher taskDispatcher) : base(bus)
        {
            _taskDispatcher = taskDispatcher;
        }

        protected override void CorrelateMessages(ICorrelationConfig<CreateOrUpdateMachineSagaData> config)
        {
            base.CorrelateMessages(config);

            config.Correlate<OperationTaskStatusEvent<PlaceVirtualMachineCommand>>(m => m.OperationId, d => d.OperationId);
            config.Correlate<OperationTaskStatusEvent<ConvergeVirtualMachineCommand>>(m => m.OperationId, d => d.OperationId);

        }

        public override Task Initiated(CreateOrUpdateMachineCommand message)
        {
            Data.Config = message.Config;

            var convergeMessage = new PlaceVirtualMachineCommand()
                { Config = message.Config, OperationId = message.OperationId, TaskId = Guid.NewGuid() };

            return _taskDispatcher.Send(convergeMessage);
        }

        public Task Handle(OperationTaskStatusEvent<PlaceVirtualMachineCommand> message)
        {
            return FailOrRun<PlaceVirtualMachineCommand,PlaceVirtualMachineResult>(message, (r) =>
            {
                var convergeMessage = new ConvergeVirtualMachineCommand
                    { Config = Data.Config, AgentName = r.AgentName, OperationId = message.OperationId, TaskId = Guid.NewGuid() };

                return _taskDispatcher.Send(convergeMessage);
            });

        }

        public Task Handle(OperationTaskStatusEvent<ConvergeVirtualMachineCommand> message)
        {
            return FailOrRun(message, () => Complete());

        }


    }
}