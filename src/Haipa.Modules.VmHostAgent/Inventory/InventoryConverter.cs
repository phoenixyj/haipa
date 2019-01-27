﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading.Tasks;
using Haipa.Messages;
using Haipa.VmManagement.Data;
using LanguageExt;

namespace Haipa.Modules.VmHostAgent.Inventory
{
    public static class InventoryConverter
    {
        public static VmStatus MapVmInfoStatusToVmStatus(VirtualMachineState valueState)
        {
            switch (valueState)
            {
                case VirtualMachineState.Other:
                    return VmStatus.Stopped;
                case VirtualMachineState.Running:
                    return VmStatus.Running;
                case VirtualMachineState.Off:
                    return VmStatus.Stopped;
                case VirtualMachineState.Stopping:
                    return VmStatus.Stopped;
                case VirtualMachineState.Saved:
                    return VmStatus.Stopped;
                case VirtualMachineState.Paused:
                    return VmStatus.Stopped;
                case VirtualMachineState.Starting:
                    return VmStatus.Stopped;
                case VirtualMachineState.Reset:
                    return VmStatus.Stopped;
                case VirtualMachineState.Saving:
                    return VmStatus.Stopped;
                case VirtualMachineState.Pausing:
                    return VmStatus.Stopped;
                case VirtualMachineState.Resuming:
                    return VmStatus.Stopped;
                case VirtualMachineState.FastSaved:
                    return VmStatus.Stopped;
                case VirtualMachineState.FastSaving:
                    return VmStatus.Stopped;
                case VirtualMachineState.ForceShutdown:
                    return VmStatus.Stopped;
                case VirtualMachineState.ForceReboot:
                    return VmStatus.Stopped;
                case VirtualMachineState.RunningCritical:
                    return VmStatus.Stopped;
                case VirtualMachineState.OffCritical:
                    return VmStatus.Stopped;
                case VirtualMachineState.StoppingCritical:
                    return VmStatus.Stopped;
                case VirtualMachineState.SavedCritical:
                    return VmStatus.Stopped;
                case VirtualMachineState.PausedCritical:
                    return VmStatus.Stopped;
                case VirtualMachineState.StartingCritical:
                    return VmStatus.Stopped;
                case VirtualMachineState.ResetCritical:
                    return VmStatus.Stopped;
                case VirtualMachineState.SavingCritical:
                    return VmStatus.Stopped;
                case VirtualMachineState.PausingCritical:
                    return VmStatus.Stopped;
                case VirtualMachineState.ResumingCritical:
                    return VmStatus.Stopped;
                case VirtualMachineState.FastSavedCritical:
                    return VmStatus.Stopped;
                case VirtualMachineState.FastSavingCritical:
                    return VmStatus.Stopped;
                default:
                    throw new ArgumentOutOfRangeException(nameof(valueState), valueState, null);
            }
        }

    }
}
