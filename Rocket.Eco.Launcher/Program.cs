﻿using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using Mono.Cecil;
using MoreLinq.Extensions;
using Rocket.Eco.Launcher.Patches;
using Rocket.Eco.Launcher.Utils;
using Rocket.Launcher.Patches;
using Rocket.Patching;
using Rocket.Patching.API;

namespace Rocket.Eco.Launcher
{
    internal static class Program
    {
        static Program()
        {
            AppDomain.CurrentDomain.AssemblyResolve += GatherRocketDependencies;

            AppDomain.CurrentDomain.AssemblyResolve += delegate(object sender, ResolveEventArgs args)
            {
                try
                {
                    AssemblyName assemblyName = new AssemblyName(args.Name);
                    Assembly[] assemblies = AppDomain.CurrentDomain.GetAssemblies();

                    return (from assembly in assemblies
                            let interatedName = assembly.GetName()
                            where string.Equals(interatedName.Name, assemblyName.Name, StringComparison.InvariantCultureIgnoreCase) && string.Equals(interatedName.CultureInfo?.Name ?? "", assemblyName.CultureInfo?.Name ?? "", StringComparison.InvariantCultureIgnoreCase)
                            select assembly).FirstOrDefault();
                }
                catch
                {
                    return null;
                }
            };
        }

        private static Assembly GatherRocketDependencies(object obj, ResolveEventArgs args) => Assembly.LoadFile(Path.Combine(Directory.GetCurrentDirectory(), "Rocket", "Binaries", args.Name.Remove(args.Name.IndexOf(",", StringComparison.InvariantCultureIgnoreCase)) + ".dll"));

        public static void Main(string[] args)
        {
            IPatchingService patchingService = new PatchingService();

            FileStream stream = File.OpenRead("EcoServer.exe");

            AssemblyDefinition defn = AssemblyDefinition.ReadAssembly(stream);

            CosturaHelper.ExtractCosturaAssemblies(defn).ForEach(x => patchingService.RegisterAssembly(x));

            patchingService.RegisterPatch<UserPatch>();
            patchingService.RegisterPatch<ChatManagerPatch>();
            patchingService.RegisterPatch<RuntimeCompilerPatch>();

            List<AssemblyDefinition> patches = patchingService.Patch().ToList();

            patches.ForEach(LoadAssemblyFromDefinition);

            CosturaHelper.DisposeStreams();

            patchingService.RegisterAssembly(defn);
            patchingService.RegisterPatch<StartupPatch>();

            patchingService.Patch().ForEach(LoadAssemblyFromDefinition);

            stream.Dispose();

            AppDomain.CurrentDomain.AssemblyResolve -= GatherRocketDependencies;

            List<string> newArgs = args.ToList();
            newArgs.Add("-nogui");

            AppDomain.CurrentDomain.GetAssemblies()
                     .First(x => x.GetName().Name.Equals("EcoServer"))
                     .GetType("Eco.Server.Startup")
                     .GetMethod("Start", BindingFlags.Static | BindingFlags.Public)
                     .Invoke(null, new object[]
                         {newArgs.ToArray()});

            foreach (string file in Directory.GetFiles(Path.Combine(Directory.GetCurrentDirectory(), "Rocket", "Binaries"))) Assembly.LoadFile(file);

            Runtime.Bootstrap();
        }

        private static void LoadAssemblyFromDefinition(AssemblyDefinition definition)
        {
            using (MemoryStream stream = new MemoryStream())
            {
                definition.Write(stream);

                stream.Position = 0; //Is this needed?

                byte[] buffer = new byte[stream.Length];

                stream.Read(buffer, 0, buffer.Length);

                Assembly.Load(buffer);
            }
        }
    }
}