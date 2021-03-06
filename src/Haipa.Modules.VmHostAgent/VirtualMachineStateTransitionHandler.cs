﻿using System.Threading.Tasks;
using Haipa.Messages;
using Haipa.Messages.Commands;
using Haipa.Messages.Operations;
using Haipa.VmManagement;
using Haipa.VmManagement.Data;
using JetBrains.Annotations;
using LanguageExt;
using Rebus.Bus;

namespace Haipa.Modules.VmHostAgent
{
    [UsedImplicitly]
    internal abstract class VirtualMachineStateTransitionHandler<T> : MachineOperationHandlerBase<T> where T : IOperationTaskCommand, IMachineCommand
    {

        public VirtualMachineStateTransitionHandler(IBus bus, IPowershellEngine engine) : base(bus, engine)
        {
        }

        protected abstract string TransitionPowerShellCommand { get; }

        protected override async Task<Either<PowershellFailure, Unit>> HandleCommand(TypedPsObject<VirtualMachineInfo> vmInfo,
            T command, IPowershellEngine engine)
        {
            var result = await engine.RunAsync(new PsCommandBuilder().AddCommand(TransitionPowerShellCommand)
                .AddParameter("VM", vmInfo.PsObject)).ConfigureAwait(false);

            return result;
        }
    }
}