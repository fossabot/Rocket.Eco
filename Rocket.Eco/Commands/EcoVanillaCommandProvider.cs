﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Eco.Gameplay.Systems.Chat;
using Rocket.API;
using Rocket.API.Commands;
using Rocket.API.DependencyInjection;
using Rocket.API.Logging;
using Rocket.Core.Logging;
using Rocket.Eco.API;

namespace Rocket.Eco.Commands
{
    /// <inheritdoc cref="ICommandProvider" />
    /// <summary>
    ///     Translates all of the commands provided by Eco and its modkit into a Rocket-useable <see cref="ICommand" />.
    /// </summary>
    public sealed class EcoVanillaCommandProvider : ContainerAccessor, ICommandProvider
    {
        private readonly List<EcoCommandWrapper> commands;

        /// <inheritdoc />
        public EcoVanillaCommandProvider(IEnumerable<ICommand> currentCommands, IDependencyContainer container) : base(container)
        {
            Dictionary<string, MethodInfo> cmds = (Dictionary<string, MethodInfo>) typeof(ChatManager).GetField("commands", BindingFlags.Instance | BindingFlags.NonPublic)?.GetValue(ChatManager.Obj);

            if (cmds == null)
                throw new Exception("A critical part of the Eco codebase has been changed; please uninstall Rocket until it is updated to support these changes.");

            ILogger logger = Container.Resolve<ILogger>();

            List<EcoCommandWrapper> tempCommands = new List<EcoCommandWrapper>();

            foreach (KeyValuePair<string, MethodInfo> pair in cmds)
            {
                ChatCommandAttribute attribute = (ChatCommandAttribute) pair.Value.GetCustomAttributes().FirstOrDefault(x => x is ChatCommandAttribute);

                if (attribute == null) continue;

                string name = attribute.UseMethodName ? pair.Value.Name : attribute.CommandName;

                foreach (ICommand command in currentCommands)
                {
                    if (!command.Name.Equals(name, StringComparison.InvariantCultureIgnoreCase) && !command.Aliases.Contains(name, StringComparer.InvariantCultureIgnoreCase)) continue;

                    logger.LogWarning($"The vanilla command \"{name}\" was not registered as an override exists.");
                    goto FAILURE;
                }

                tempCommands.Add(new EcoCommandWrapper(pair.Value, attribute, Container));

                FAILURE: ;
            }

            commands = tempCommands;
        }

        /// <inheritdoc />
        public ILifecycleObject GetOwner(ICommand command) => Container.Resolve<IImplementation>();

        /// <inheritdoc />
        public IEnumerable<ICommand> Commands => commands;
    }
}