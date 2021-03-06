﻿using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.IO;
using System.Linq;
using System.Linq.Expressions;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Haipa.CloudInit.ConfigDrive.Generator;
using Haipa.CloudInit.ConfigDrive.NoCloud;
using Haipa.CloudInit.ConfigDrive.Processing;
using Haipa.Modules.VmHostAgent;
using Haipa.VmConfig;
using Haipa.VmManagement.Data;
using LanguageExt;
using LanguageExt.UnsafeValueAccess;
using Newtonsoft.Json.Linq;
using static LanguageExt.Prelude;

namespace Haipa.VmManagement
{
    public static class Converge
    {
#pragma warning disable 1998
        public static async Task<Either<PowershellFailure, MachineConfig>> NormalizeMachineConfig(
#pragma warning restore 1998
            MachineConfig config,  IPowershellEngine engine, Func<string, Task> reportProgress)
        {
            var machineConfig = config;

            if (machineConfig.VM== null)
                machineConfig.VM = new VirtualMachineConfig();

            if (string.IsNullOrWhiteSpace(machineConfig.Name) && string.IsNullOrWhiteSpace(machineConfig.Id))
            {
                //TODO generate a random name here
                machineConfig.Name = "haipa-machine";
            }


            if (machineConfig.VM.Cpu == null)
                machineConfig.VM.Cpu = new VirtualMachineCpuConfig {Count = 1};

            if (machineConfig.VM.Memory == null)
                machineConfig.VM.Memory = new VirtualMachineMemoryConfig() { Startup = 1024 };

            if (machineConfig.VM.Drives == null)
                machineConfig.VM.Drives = new List<VirtualMachineDriveConfig>();

            if (machineConfig.VM.NetworkAdapters == null)
                machineConfig.VM.NetworkAdapters = new List<VirtualMachineNetworkAdapterConfig>();

            if (machineConfig.Provisioning == null)
                machineConfig.Provisioning = new VirtualMachineProvisioningConfig();

            if (string.IsNullOrWhiteSpace(machineConfig.Provisioning.Hostname))
                machineConfig.Provisioning.Hostname = machineConfig.Name;

            foreach (var adapterConfig in machineConfig.VM.NetworkAdapters)
            {
                if (adapterConfig.MacAddress != null)
                {
                    adapterConfig.MacAddress = adapterConfig.MacAddress.Replace("-", "");
                    adapterConfig.MacAddress = adapterConfig.MacAddress.Replace(":", "");
                    adapterConfig.MacAddress = adapterConfig.MacAddress.ToLowerInvariant();
                }
                else
                {
                    adapterConfig.MacAddress = "";
                }
            }

            foreach (var driveConfig in machineConfig.VM.Drives)
            {
                if (!driveConfig.Type.HasValue)
                    driveConfig.Type = VirtualMachineDriveType.VHD;

                if (driveConfig.Size == 0)
                    driveConfig.Size = null;
            }

            await reportProgress($"Converging virtual machine '{config.Name}'").ConfigureAwait(false);

            return machineConfig;
        }


        public static async Task<Either<PowershellFailure, TypedPsObject<VirtualMachineInfo>>> Firmware(TypedPsObject<VirtualMachineInfo> vmInfo,
            MachineConfig config, IPowershellEngine engine, Func<string, Task> reportProgress)
        {
            if (vmInfo.Value.Generation < 2)
                return vmInfo;

            return await engine.GetObjectsAsync<VMFirmwareInfo>(PsCommandBuilder.Create()
                    .AddCommand("Get-VMFirmware")
                    .AddParameter("VM", vmInfo.PsObject))
                .BindAsync(firmwareInfoSeq =>
                    firmwareInfoSeq.HeadOrNone()
                        .Match<Either<PowershellFailure, TypedPsObject<VMFirmwareInfo>>>(
                            None: () => new PowershellFailure { Message = "Failed to get VM Firmware" },
                            Some: s => s
                        ))         
                .BindAsync(async firmwareInfo =>
                    {
                        if (firmwareInfo.Value.SecureBoot != OnOffState.Off)
                        {
                            await reportProgress($"Configure VM Firmware - Secure Boot: {OnOffState.Off}")
                                .ConfigureAwait(false);


                            var res= await engine.RunAsync(PsCommandBuilder.Create()
                                .AddCommand("Set-VMFirmware")
                                .AddParameter("VM", vmInfo.PsObject)
                                .AddParameter("EnableSecureBoot", OnOffState.Off)).BindAsync(_ => vmInfo.RecreateOrReload(engine)
                            ).ConfigureAwait(false);
                            return res;
                        }

                        return await vmInfo.RecreateOrReload(engine).ConfigureAwait(false);
                    }
                ).ConfigureAwait(false);

        }

        public static  async Task<Either<PowershellFailure, TypedPsObject<VirtualMachineInfo>>> Cpu(TypedPsObject<VirtualMachineInfo> vmInfo,
            Option<VirtualMachineCpuConfig> cpuConfig, IPowershellEngine engine, Func<string, Task> reportProgress)
        {
            return await cpuConfig.MatchAsync(
                None: () => vmInfo,
                Some: async config =>
                {
                    if (vmInfo.Value.ProcessorCount != config.Count)
                    {
                        await reportProgress($"Configure VM Processor: Count: {config.Count}").ConfigureAwait(false);

                        await engine.RunAsync(PsCommandBuilder.Create()
                            .AddCommand("Set-VMProcessor")
                            .AddParameter("VM", vmInfo.PsObject)
                            .AddParameter("Count", config.Count)).ConfigureAwait(false);

                        return await vmInfo.RecreateOrReload(engine).ConfigureAwait(false);
                    }

                    return vmInfo;
                }).ConfigureAwait(false);

        }


        public static async Task Definition(
            IPowershellEngine engine,
            TypedPsObject<VirtualMachineInfo> vmInfo,
            MachineConfig vmConfig,
            Func<string, Task> reportProgress)
        {

            if (vmInfo.Value.Generation >= 2)
            {
                await engine.GetObjectsAsync<VMFirmwareInfo>(PsCommandBuilder.Create()
                    .AddCommand("Get-VMFirmware")
                    .AddParameter("VM", vmInfo.PsObject)).MapAsync(async (firmwareInfo) =>
                {
                    if (firmwareInfo.Head.Value.SecureBoot != OnOffState.Off)
                    {
                        await reportProgress($"Configure VM Firmware - Secure Boot: {OnOffState.Off}")
                            .ConfigureAwait(false);

                        await engine.RunAsync(PsCommandBuilder.Create()
                            .AddCommand("Set-VMFirmware")
                            .AddParameter("VM", vmInfo.PsObject)
                            .AddParameter("EnableSecureBoot", OnOffState.Off)).ConfigureAwait(false);


                    }
                }).ConfigureAwait(false);
            }


            if (vmInfo.Value.ProcessorCount != vmConfig.VM.Cpu.Count)
            {
                await reportProgress($"Configure VM Processor: Count: {vmConfig.VM.Cpu.Count}").ConfigureAwait(false);

                await engine.RunAsync(PsCommandBuilder.Create()
                    .AddCommand("Set-VMProcessor")
                    .AddParameter("VM", vmInfo.PsObject)
                    .AddParameter("Count", vmConfig.VM.Cpu.Count)).ConfigureAwait(false);
            }

            var memoryStartupBytes = vmConfig.VM.Memory.Startup * 1024L * 1024;

            if (vmInfo.Value.MemoryStartup != memoryStartupBytes)
            {
                await reportProgress($"Configure VM Memory: Startup: {vmConfig.VM.Memory.Startup} MB").ConfigureAwait(false);

                await engine.RunAsync(PsCommandBuilder.Create()
                    .AddCommand("Set-VMMemory")
                    .AddParameter("VM", vmInfo.PsObject)
                    .AddParameter("StartupBytes", memoryStartupBytes)).ConfigureAwait(false);

            }
        }


        public static async Task<Either<PowershellFailure, TypedPsObject<VirtualMachineInfo>>> Drives(TypedPsObject<VirtualMachineInfo> vmInfo,
            MachineConfig vmConfig,
            VMStorageSettings storageSettings,
            HostSettings hostSettings, IPowershellEngine engine, Func<string, Task> reportProgress)
        {

            if(storageSettings.Frozen)
                return Right<PowershellFailure, TypedPsObject<VirtualMachineInfo>>(vmInfo);

            var currentCheckpointType = vmInfo.Value.CheckpointType;

            try
            {
                await (
                    //prevent snapshots creating during running disk converge
                    from _ in SetVMCheckpointType(vmInfo, CheckpointType.Disabled, engine).ToAsync()
                    //make a plan
                    from plannedDriveStorageSettings in Storage.PlanDriveStorageSettings(vmConfig, storageSettings, hostSettings, engine).ToAsync()
                    //ensure that the changes reflect the current VM settings
                    from infoReloaded in vmInfo.Reload(engine).ToAsync()
                    //detach removed disks
                    from __ in DetachUndefinedDrives(engine, infoReloaded, plannedDriveStorageSettings, hostSettings, reportProgress).ToAsync()
                    from infoRecreated in vmInfo.RecreateOrReload(engine).ToAsync()
                    from ___ in VirtualDisks(infoRecreated, plannedDriveStorageSettings, hostSettings, engine,reportProgress).ToAsync()

                    select Unit.Default).ToEither().ConfigureAwait(false);
            }
            finally
            {
                await SetVMCheckpointType(vmInfo, currentCheckpointType, engine).ConfigureAwait(false);
            }

            return await vmInfo.Reload(engine).ConfigureAwait(false);
        }

        public static Task<Either<PowershellFailure, Unit>> VirtualDisks(TypedPsObject<VirtualMachineInfo> vmInfo,
            Seq<VMDriveStorageSettings> plannedDriveStorageSettings, HostSettings hostSettings, IPowershellEngine engine, Func<string, Task> reportProgress)
        {
            var plannedDiskSettings = plannedDriveStorageSettings
                .Where(x => x.Type == VirtualMachineDriveType.VHD || x.Type == VirtualMachineDriveType.SharedVHD)
                .Cast<VMDiskStorageSettings>().ToSeq();

            return (from currentDiskSettings in Storage.DetectDiskStorageSettings(vmInfo.Value.HardDrives, hostSettings, engine)
                    from _ in plannedDiskSettings.MapToEitherAsync(s => VirtualDisk(s, engine, vmInfo, currentDiskSettings, reportProgress))
                    select Unit.Default);

        }


        public static Task<Either<PowershellFailure, Unit>> SetVMCheckpointType(TypedPsObject<VirtualMachineInfo> vmInfo, CheckpointType checkpointType, IPowershellEngine engine)
        {
            return engine.RunAsync(new PsCommandBuilder()
                .AddCommand("Set-VM")
                .AddParameter("VM", vmInfo.PsObject)
                .AddParameter("CheckpointType", checkpointType));

        }

        public static Task<Either<PowershellFailure, Unit>> DetachUndefinedDrives(
            IPowershellEngine engine,
            TypedPsObject<VirtualMachineInfo> vmInfo,
            Seq<VMDriveStorageSettings> plannedStorageSettings,
            HostSettings hostSettings,
            Func<string, Task> reportProgress)

        {

            return (from currentDiskSettings in Storage.DetectDiskStorageSettings(vmInfo.Value.HardDrives,
                    hostSettings, engine).ToAsync()
                from _ in DetachUndefinedHardDrives(engine, vmInfo, plannedStorageSettings,
                    currentDiskSettings, reportProgress).ToAsync()
                from __ in DetachUndefinedDvdDrives(engine, vmInfo, plannedStorageSettings, reportProgress).ToAsync()
                select Unit.Default).ToEither();
        }


        public static Task<Either<PowershellFailure, Unit>> DetachUndefinedHardDrives(
            IPowershellEngine engine, 
            TypedPsObject<VirtualMachineInfo> vmInfo,
            Seq<VMDriveStorageSettings> plannedStorageSettings,
            Seq<CurrentVMDiskStorageSettings> currentDiskStorageSettings,  
            Func<string, Task> reportProgress)

        {
            var planedDiskSettings = plannedStorageSettings.Where(x =>
                    x.Type == VirtualMachineDriveType.VHD || x.Type == VirtualMachineDriveType.SharedVHD)
                .Cast<VMDiskStorageSettings>().ToSeq();

            var attachedPaths = planedDiskSettings.Map(s => s.AttachPath).Map(x => x.IfNone(""))
                .Where(x => !string.IsNullOrWhiteSpace(x));

            var frozenDiskIds = currentDiskStorageSettings.Where(x => x.Frozen).Map(x=>x.AttachedVMId);

            return FindAndApply(vmInfo,
                    i => i.HardDrives,
                    hd =>
                    {
                        var plannedDiskAtControllerPos = planedDiskSettings
                            .FirstOrDefault(x =>
                                x.ControllerLocation == hd.ControllerLocation && x.ControllerNumber == hd.ControllerNumber);

                        var detach = plannedDiskAtControllerPos==null;

                        if (!detach && plannedDiskAtControllerPos.AttachPath.IsSome)
                        {
                            var plannedAttachPath = plannedDiskAtControllerPos.AttachPath.IfNone("");
                            if (hd.Path==null || !hd.Path.Equals(plannedAttachPath, StringComparison.InvariantCultureIgnoreCase))
                                detach = true;
                        }

                        if (detach && frozenDiskIds.Contains(hd.Id))
                        {
                            reportProgress(hd.Path != null
                                ? $"Skipping detach of frozen disk {Path.GetFileNameWithoutExtension(hd.Path)}"
                                : $"Skipping detach of unknown frozen disk at controller {hd.ControllerNumber}, Location: {hd.ControllerLocation}");

                            return false;
                        }

                        if (detach)
                        {
                            reportProgress(hd.Path != null
                                ? $"Detaching disk {Path.GetFileNameWithoutExtension(hd.Path)} from controller: {hd.ControllerNumber}, Location: {hd.ControllerLocation}"
                                : $"Detaching unknown disk at controller: {hd.ControllerNumber}, Location: {hd.ControllerLocation}");
                        }

                        return detach;
                    },
                    i => engine.RunAsync(PsCommandBuilder.Create().AddCommand("Remove-VMHardDiskDrive")
                        .AddParameter("VMHardDiskDrive", i.PsObject))).Map(x => x.Lefts().HeadOrNone())
                .MatchAsync(
                    Some: l => LeftAsync<PowershellFailure, Unit>(l).ToEither(),
                    None: () => RightAsync<PowershellFailure, Unit>(Unit.Default)
                        .ToEither());

        }


        public static Task<Either<PowershellFailure, Unit>> DetachUndefinedDvdDrives(
            IPowershellEngine engine,
            TypedPsObject<VirtualMachineInfo> vmInfo,
            Seq<VMDriveStorageSettings> plannedStorageSettings,
            Func<string, Task> reportProgress)

        {

            var controllersAndLocations = plannedStorageSettings.Where(x=>x.Type==VirtualMachineDriveType.DVD)
                .Map(x => new { x.ControllerNumber, x.ControllerLocation })               
                .GroupBy(x => x.ControllerNumber)
                .ToImmutableDictionary(x => x.Key, x => x.Map(y => y.ControllerLocation).ToImmutableArray());


            return FindAndApply(vmInfo,
                    i => i.DVDDrives,
                    hd =>
                    {

                        //ignore cloud init drive, will be handled later
                        if (hd.ControllerLocation == 63 && hd.ControllerNumber == 0)
                            return false;

                        var detach = !controllersAndLocations.ContainsKey(hd.ControllerNumber) ||
                                     !controllersAndLocations[hd.ControllerNumber].Contains(hd.ControllerLocation);

                        return detach;
                    },
                    i => engine.RunAsync(PsCommandBuilder.Create().AddCommand("Remove-VMDvdDrive")
                        .AddParameter("VMDvdDrive", i.PsObject))).Map(x => x.Lefts().HeadOrNone())
                .MatchAsync(
                    Some: l => LeftAsync<PowershellFailure, Unit>(l).ToEither(),
                    None: () => RightAsync<PowershellFailure, Unit>(Unit.Default)
                        .ToEither());

        }


        public static async Task<Either<PowershellFailure, TypedPsObject<VirtualMachineInfo>>> VirtualDisk(    
                VMDiskStorageSettings diskSettings,
                IPowershellEngine engine,
                TypedPsObject<VirtualMachineInfo> vmInfo,
                Seq<CurrentVMDiskStorageSettings> currentStorageSettings,
                Func<string,Task> reportProgress)
        {

            var currentSettings =
                currentStorageSettings.Find(x => diskSettings.Path.Equals(x.Path, StringComparison.OrdinalIgnoreCase) 
                && diskSettings.Name.Equals(x.Name, StringComparison.InvariantCultureIgnoreCase) );
            var frozenOptional = currentSettings.Map(x => x.Frozen);

            if (frozenOptional.IsSome && frozenOptional.ValueUnsafe())
            {
                await reportProgress($"Skipping disk '{diskSettings.Name}': storage management is disabled for this disk.").ConfigureAwait(false);
                return vmInfo;
            }



            return await diskSettings.AttachPath.Map(async (vhdPath) =>
            {

                if (!File.Exists(vhdPath))
                {
                    await reportProgress($"Create VHD: {diskSettings.Name}").ConfigureAwait(false);

                    var createDiskResult = await diskSettings.ParentPath.Match(Some: parentPath =>
                        {
                            return engine.RunAsync(PsCommandBuilder.Create().Script(
                                $"New-VHD -Path \"{vhdPath}\" -ParentPath \"{parentPath}\" -Differencing"));
                        },
                        None: () =>
                        {
                            return engine.RunAsync(PsCommandBuilder.Create().Script(
                                $"New-VHD -Path \"{vhdPath}\" -Dynamic -SizeBytes {diskSettings.SizeBytes}"));
                        });

                    if (createDiskResult.IsLeft)
                        return Prelude.Left(createDiskResult.LeftAsEnumerable().FirstOrDefault());
                }

                var sizeResult = await engine
                    .GetObjectsAsync<VhdInfo>(new PsCommandBuilder().AddCommand("get-vhd").AddArgument(vhdPath))
                    .BindAsync(x => x.HeadOrLeft(new PowershellFailure())).BindAsync(async (vhdInfo) =>
                    {
                        if (vhdInfo.Value.Size != diskSettings.SizeBytes && diskSettings.SizeBytes > 0)
                        {
                            var gb = Math.Round(diskSettings.SizeBytes / 1024d / 1024 / 1024, 1);
                            await reportProgress(
                                $"Resizing disk {diskSettings.Name} to {gb} GB");
                            return await engine.RunAsync(PsCommandBuilder.Create().AddCommand("Resize-VHD")
                                .AddArgument(vhdPath)
                                .AddParameter("Size", diskSettings.SizeBytes));

                        }

                        return Unit.Default;
                    });

                if (sizeResult.IsLeft)
                    return Prelude.Left(sizeResult.LeftAsEnumerable().FirstOrDefault());


                return await GetOrCreateInfoAsync(vmInfo,
                    i => i.HardDrives,
                    disk => currentSettings.Map(x=>x.AttachedVMId) == disk.Id,
                    async () =>
                    {
                        await reportProgress($"Attaching disk {diskSettings.Name} to controller: {diskSettings.ControllerNumber}, Location: {diskSettings.ControllerLocation}").ConfigureAwait(false);
                        return (await engine.GetObjectsAsync<HardDiskDriveInfo>(PsCommandBuilder.Create()
                            .AddCommand("Add-VMHardDiskDrive")
                            .AddParameter("VM", vmInfo.PsObject)
                            .AddParameter("Path", vhdPath)
                            .AddParameter("ControllerNumber", diskSettings.ControllerNumber)
                            .AddParameter("ControllerLocation", diskSettings.ControllerLocation)
                            .AddParameter("PassThru")
                        ).ConfigureAwait(false));

                    }).BindAsync(_ => vmInfo.RecreateOrReload(engine))
                    
                    
                    .ConfigureAwait(false);

            }).IfNone(vmInfo.RecreateOrReload(engine));

        }


        public static async Task<Either<PowershellFailure, TypedPsObject<VirtualMachineInfo>>> NetworkAdapters(TypedPsObject<VirtualMachineInfo> vmInfo,
            Seq<VirtualMachineNetworkAdapterConfig> adapterConfig, MachineConfig vmConfig, IPowershellEngine engine, Func<string, Task> reportProgress)
        {
            var seq = adapterConfig.Map(adapter => NetworkAdapter(adapter, engine, vmInfo, vmConfig, reportProgress));

            if (seq.IsEmpty)
                return vmInfo;

            return await seq.Last;
        }

        public static async Task<Either<PowershellFailure, TypedPsObject<VirtualMachineInfo>>> NetworkAdapter(
            VirtualMachineNetworkAdapterConfig networkAdapterConfig,
            IPowershellEngine engine,
            TypedPsObject<VirtualMachineInfo> vmInfo,
            MachineConfig machineConfig,
            Func<string, Task> reportProgress)
        {


            var optionalAdapter = await GetOrCreateInfoAsync(vmInfo,
                i => i.NetworkAdapters,
                adapter => networkAdapterConfig.Name.Equals(adapter.Name, StringComparison.OrdinalIgnoreCase),
                async () =>
                {
                    await reportProgress($"Add Network Adapter: {networkAdapterConfig.Name}").ConfigureAwait(false);
                    return await engine.GetObjectsAsync<VMNetworkAdapter>(PsCommandBuilder.Create()
                        .AddCommand("Add-VmNetworkAdapter")
                        .AddParameter("Passthru")
                        .AddParameter("VM", vmInfo.PsObject)
                        .AddParameter("Name", networkAdapterConfig.Name)
                        .AddParameter("StaticMacAddress", UseOrGenerateMacAddress(networkAdapterConfig, vmInfo))
                        .AddParameter("SwitchName", networkAdapterConfig.SwitchName)).ConfigureAwait(false);

                }).ConfigureAwait(false);

            return await optionalAdapter.BindAsync(_ => vmInfo.RecreateOrReload(engine)).ConfigureAwait(false);

            //optionalAdapter.Map(async (adapter) =>
            //{
            //    if (!adapter.Value.Connected || adapter.Value.SwitchName != networkConfig.SwitchName)
            //    {
            //        await reportProgress($"Connect Network Adapter {adapter.Value.Name} to switch {networkConfig.SwitchName}").ConfigureAwait(false);
            //        await engine.RunAsync(
            //            PsCommandBuilder.Create().AddCommand("Connect-VmNetworkAdapter")
            //                .AddParameter("VMNetworkAdapter", adapter.PsObject)
            //                .AddParameter("SwitchName", networkConfig.SwitchName)).ConfigureAwait(false);

            //    }
            //});


        }

        private static string UseOrGenerateMacAddress(VirtualMachineNetworkAdapterConfig adapterConfig, TypedPsObject<VirtualMachineInfo> vmInfo)
        {
            var result = adapterConfig.MacAddress;
            if (string.IsNullOrWhiteSpace(result))
                result = GenerateMacAddress(vmInfo.Value.Id, adapterConfig.Name);
            return result;
        }

        private static string GenerateMacAddress(Guid valueId, string adapterName)
        {
            var id = $"{valueId}_{adapterName}";
            var crc = new Crc32();

            string result = null;

            var arrayData = Encoding.ASCII.GetBytes(id);
            var arrayResult = crc.ComputeHash(arrayData);
            foreach (var t in arrayResult)
            {
                var temp = Convert.ToString(t, 16);
                if (temp.Length == 1)
                    temp = $"0{temp}";
                result += temp;
            }
            return "d2ab" + result;
        }

 

        private static async Task<Either<PowershellFailure, TypedPsObject<TSub>>> GetOrCreateInfoAsync<T, TSub>(TypedPsObject<T> parentInfo,
            Expression<Func<T, IList<TSub>>> listProperty,
            Func<TSub, bool> predicateFunc,
            Func<Task<Either<PowershellFailure, Seq<TypedPsObject<TSub>>>>> creatorFunc)
        {
            var result = parentInfo.GetList(listProperty, predicateFunc).ToArray();

            if (result.Length() != 0)
                return Try(result.Single()).Try().Match<Either<PowershellFailure, TypedPsObject<TSub>>>(
                    Fail: ex =>
                    {
                        return Left(new PowershellFailure {Message = ex.Message});
                    },
                    Succ: x => Right(x)
                );


            var creatorResult = await creatorFunc().ConfigureAwait(false);

            var res = creatorResult.Bind(
                seq => seq.HeadOrNone().ToEither(() =>
                    new PowershellFailure {Message = "Object creation was successful, but no result was returned."}));

            return res;
        }

        public static Task<Either<PowershellFailure,TypedPsObject<VirtualMachineInfo>>> CreateVirtualMachine(
            IPowershellEngine engine,
            string vmName,
            string storageIdentifier,
            string vmPath,
            int startupMemory)
        {
            var memoryStartupBytes = startupMemory * 1024L * 1024;


            return engine.GetObjectsAsync<VirtualMachineInfo>(PsCommandBuilder.Create()
                    .AddCommand("New-VM")
                    .AddParameter("Name", storageIdentifier)
                    .AddParameter("Path", vmPath)
                    .AddParameter("MemoryStartupBytes", memoryStartupBytes)
                    .AddParameter("Generation", 2))
                .MapAsync(x => x.Head).MapAsync(
                    result =>
                    {

                        engine.RunAsync(PsCommandBuilder.Create().AddCommand("Get-VMNetworkAdapter")
                            .AddParameter("VM", result.PsObject).AddCommand("Remove-VMNetworkAdapter"));

                        return result;
                    })
                .BindAsync(info => RenameVirtualMachine(engine, info, vmName));

        }

        public static Task<Either<PowershellFailure, TypedPsObject<VirtualMachineInfo>>> RenameVirtualMachine(
            IPowershellEngine engine,
            TypedPsObject<VirtualMachineInfo> vmInfo,
            string newName)
        {
            return engine.RunAsync(PsCommandBuilder.Create()
                .AddCommand("Rename-VM")
                .AddParameter("VM", vmInfo.PsObject)
                .AddParameter("NewName", newName)
            ).BindAsync(u => vmInfo.RecreateOrReload(engine));

        }

        private static async Task ReportPowershellProgress(string taskName, int progress, Func<string, Task> progressMessage)
        {
            await progressMessage($"{taskName}: {progress} % completed").ConfigureAwait(false);
        }


        private static Either<PowershellFailure, Unit> CreateConfigDriveDirectory(string configDrivePath)
        {
            if (Directory.Exists(configDrivePath)) return Unit.Default;

            var tryResult = Try(Directory.CreateDirectory(configDrivePath)).Try();

            if (tryResult.IsFaulted)
                return new PowershellFailure { Message = $"Failed to create directory {configDrivePath}" };

            return Unit.Default;
        }

        private static Task<Either<PowershellFailure, TypedPsObject<VirtualMachineInfo>>> EjectConfigDriveDisk(
            TypedPsObject<VirtualMachineInfo> vmInfo,
            IPowershellEngine engine)
        {
            return FindAndApply(vmInfo, l => l.DVDDrives,
                drive => drive.ControllerLocation == 63 && drive.ControllerNumber == 0,
                drive => engine.RunAsync(PsCommandBuilder.Create()
                    .AddCommand("Set-VMDvdDrive")
                    .AddParameter("VMDvdDrive", drive.PsObject)
                    .AddParameter("Path", null)))

                .Map(list => list.Lefts().HeadOrNone()).MatchAsync(
                    None: () =>
                    {
                        return vmInfo.RecreateOrReload(engine);
                    },
                    Some: l => LeftAsync<PowershellFailure, TypedPsObject<VirtualMachineInfo>>(l).ToEither());

        }

        private static Task<Either<PowershellFailure, TypedPsObject<VirtualMachineInfo>>> RemoveConfigDriveDisk(
            TypedPsObject<VirtualMachineInfo> vmInfo,
            IPowershellEngine engine)
        {
            return FindAndApply(vmInfo, l => l.DVDDrives,
                    drive => drive.ControllerLocation == 63 && drive.ControllerNumber == 0,
                    drive => engine.RunAsync(PsCommandBuilder.Create()
                        .AddCommand("Remove-VMDvdDrive")
                        .AddParameter("VMDvdDrive", drive.PsObject)))
                .Map(list => list.Lefts().HeadOrNone()).MatchAsync(
                    None: () => vmInfo.RecreateOrReload(engine),
                    Some: l => LeftAsync<PowershellFailure, TypedPsObject<VirtualMachineInfo>>(l).ToEither());

        }

        private static Task<Either<PowershellFailure, TypedPsObject<VirtualMachineInfo>>> InsertConfigDriveDisk(
            string configDriveIsoPath,
            TypedPsObject<VirtualMachineInfo> vmInfo,
            IPowershellEngine engine)
        {

            return
                from dvdDrive in GetOrCreateInfoAsync(vmInfo,
                    l => l.DVDDrives,
                    drive => drive.ControllerLocation == 63 && drive.ControllerNumber == 0,
                    () => engine.GetObjectsAsync<DvdDriveInfo>(
                        PsCommandBuilder.Create().AddCommand("Add-VMDvdDrive")
                            .AddParameter("VM", vmInfo.PsObject)
                            .AddParameter("ControllerNumber", 0)
                            .AddParameter("ControllerLocation", 63)
                            .AddParameter("PassThru"))
                )

                from _ in engine.RunAsync(PsCommandBuilder.Create()

                    .AddCommand("Set-VMDvdDrive")
                    .AddParameter("VMDvdDrive", dvdDrive.PsObject)
                    .AddParameter("Path", configDriveIsoPath))          
                    
                  from vmInfoRecreated in vmInfo.RecreateOrReload(engine)
                select vmInfoRecreated;

        }


        private static void GenerateConfigDriveDisk(string configDriveIsoPath,
            string hostname,
            JObject userData)
        {
            try
            {
                GeneratorBuilder.Init()
                    .NoCloud(new NoCloudConfigDriveMetaData(hostname))
                    .SwapFile()
                    .UserData(userData)
                    .Processing()
                    .Image().ImageFile(configDriveIsoPath)
                    .Generate();
            }
            catch (Exception ex)
            {
                return;
            }

            return;
        }

        public static async Task<Either<PowershellFailure, TypedPsObject<VirtualMachineInfo>>> CloudInit(
            TypedPsObject<VirtualMachineInfo> vmInfo,
            string vmConfigPath,
            VirtualMachineProvisioningConfig provisioningConfig,
            IPowershellEngine engine,
            Func<string, Task<Unit>> reportProgress
            )
        {

            var remove = await RemoveConfigDriveDisk(vmInfo, engine).BindAsync(u => vmInfo.RecreateOrReload(engine)).ConfigureAwait(false);


            if (provisioningConfig.Method == ProvisioningMethod.None || remove.IsLeft)
            {
                return remove;
            }

            
            var configDriveIsoPath = Path.Combine(vmConfigPath, "configdrive.iso");

            await reportProgress("Updating configdrive disk").ConfigureAwait(false);

            await from _ in CreateConfigDriveDirectory(vmConfigPath).AsTask()
                      select _;

            GenerateConfigDriveDisk(configDriveIsoPath, provisioningConfig.Hostname,
                provisioningConfig.UserData);

            return await InsertConfigDriveDisk(configDriveIsoPath, vmInfo, engine);

        }

        private static Task<IEnumerable<TRes>> FindAndApply<T, TSub, TRes>(TypedPsObject<T> parentInfo,
            Expression<Func<T, IList<TSub>>> listProperty,
            Func<TSub, bool> predicateFunc,
            Func<TypedPsObject<TSub>, Task<TRes>> applyFunc)
        {
            return parentInfo.GetList(listProperty, predicateFunc).ToArray().Map(applyFunc)
                .Traverse(l => l);
        }
    }

    public class StorageNames
    {
        public Option<string> DataStoreName { get; set; }
        public Option<string> ProjectName { get; set; }
        public Option<string> EnvironmentName { get; set; }

    }

    public class VMStorageSettings
    {
        public StorageNames StorageNames { get; set; }
        public Option<string> StorageIdentifier { get; set; }

        public string VMPath { get; set; }
        public bool Frozen { get; set; }
    }

    public class VMDriveStorageSettings
    {
        public VirtualMachineDriveType Type { get; set; }

        public int ControllerLocation { get; set; }
        public int ControllerNumber { get; set; }
    }

    public class VMDiskStorageSettings : VMDriveStorageSettings
    {
        public string Name { get; set; }
        public string Path { get; set; }
        public Option<string> ParentPath { get; set; }
        public Option<string> AttachPath { get; set; }

        public Option<string> StorageIdentifier { get; set; }
        public StorageNames StorageNames { get; set; }
        public long SizeBytes { get; set; }

    }

    public class VMDVdStorageSettings : VMDriveStorageSettings
    {

    }

    public class CurrentVMDiskStorageSettings : VMDiskStorageSettings
    {
        public bool Frozen { get; set; }
        public string AttachedVMId { get; set; }
    }


}