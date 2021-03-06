﻿using System;
using System.Threading.Tasks;
using Haipa.Messages;
using Haipa.Messages.Commands;
using Haipa.Messages.Operations;
using Haipa.VmManagement;
using Haipa.VmManagement.Data;
using LanguageExt;
using Rebus.Bus;
using Rebus.Handlers;
// ReSharper disable ArgumentsStyleAnonymousFunction

namespace Haipa.Modules.VmHostAgent
{
    internal abstract class MachineOperationHandlerBase<T> : IHandleMessages<AcceptedOperationTask<T>> where T: IOperationTaskCommand, IMachineCommand
    {
        private readonly IPowershellEngine _engine;
        private readonly IBus _bus;

        protected MachineOperationHandlerBase(IBus bus, IPowershellEngine engine)
        {
            _bus = bus;
            _engine = engine;
        }

        protected abstract Task<Either<PowershellFailure, Unit>> HandleCommand(
            TypedPsObject<VirtualMachineInfo> vmInfo, T command, IPowershellEngine engine);

        public async Task Handle(AcceptedOperationTask<T> message)
        {
            var command = message.Command;

            var result = await GetVmInfo(command.MachineId, _engine)            
                .BindAsync(optVmInfo =>
                {
                    return optVmInfo.MatchAsync(
                        Some: s => HandleCommand(s, command, _engine),
                        None: () => Unit.Default);
                }).ConfigureAwait(false);
            
            await result.MatchAsync(
                LeftAsync: f => HandleError(f,command),
                RightAsync: async _ =>
                {
                    await _bus.Publish(OperationTaskStatusEvent.Completed(command.OperationId, command.TaskId))
                        .ConfigureAwait(false);

                    return Unit.Default;
                }).ConfigureAwait(false);
        }

        private async Task<Unit> HandleError(PowershellFailure failure, IOperationTaskMessage command)
        {
            await _bus.Publish(OperationTaskStatusEvent.Failed(command.OperationId, command.TaskId, 
                new ErrorData { ErrorMessage = failure.Message

            })).ConfigureAwait(false);

            return Unit.Default;
        }

        private Task<Either<PowershellFailure, Option<TypedPsObject<VirtualMachineInfo>>>> GetVmInfo(Guid vmId,
            IPowershellEngine engine)
        {
            return engine.GetObjectsAsync<VirtualMachineInfo>(CreateGetVMCommand(vmId)).MapAsync(seq => seq.HeadOrNone());
        }

        protected virtual PsCommandBuilder CreateGetVMCommand(Guid vmId)
        {
            return PsCommandBuilder.Create()
                .AddCommand("get-vm").AddParameter("Id", vmId);
        }
    }
}