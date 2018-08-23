﻿using System;
using System.Threading.Tasks;
using HyperVPlus.VmManagement;
using HyperVPlus.VmManagement.Data;
using LanguageExt;

namespace HyperVPlus.Agent
{
    internal interface IVirtualMachineInfoProvider
    {
        Task<Either<PowershellFailure,TypedPsObject<VirtualMachineInfo>>> GetInfoAsync(string vmName);
        Task<Either<PowershellFailure, TypedPsObject<VirtualMachineInfo>>> GetInfoAsync(Guid id);
        void SetInfo(TypedPsObject<VirtualMachineInfo> info);
        void ClearInfo(Guid id);

    }

}