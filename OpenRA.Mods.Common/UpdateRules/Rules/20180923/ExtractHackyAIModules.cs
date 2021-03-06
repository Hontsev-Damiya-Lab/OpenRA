#region Copyright & License Information
/*
 * Copyright 2007-2018 The OpenRA Developers (see AUTHORS)
 * This file is part of OpenRA, which is free software. It is made
 * available to you under the terms of the GNU General Public License
 * as published by the Free Software Foundation, either version 3 of
 * the License, or (at your option) any later version. For more
 * information, see COPYING.
 */
#endregion

using System.Collections.Generic;
using System.Linq;

namespace OpenRA.Mods.Common.UpdateRules.Rules
{
	public class ExtractHackyAIModules : UpdateRule
	{
		public override string Name { get { return "Split HackyAI logic handling to BotModules"; } }
		public override string Description
		{
			get
			{
				return "Most properties and logic are being moved from HackyAI\n" +
					"to *BotModules.";
			}
		}

		readonly List<string> locations = new List<string>();
		bool messageShown;

		readonly string[] harvesterFields =
		{
			"HarvesterEnemyAvoidanceRadius", "AssignRolesInterval"
		};

		readonly string[] supportPowerFields =
		{
			"SupportPowerDecisions"
		};

		public override IEnumerable<string> AfterUpdate(ModData modData)
		{
			if (!messageShown)
				yield return "You may want to check your AI yamls for possible redundant module entries.\n" +
					"Additionally, make sure the Player actor has the ConditionManager trait and add it manually if it doesn't.";

			messageShown = true;

			if (locations.Any())
				yield return "This update rule can only autoamtically update the base HackyAI definitions,\n" +
					"not any overrides in other files (unless they redefine Type).\n" +
					"You will have to manually check and possibly update the following locations:\n" +
					UpdateUtils.FormatMessageList(locations);

			locations.Clear();
		}

		public override IEnumerable<string> UpdateActorNode(ModData modData, MiniYamlNode actorNode)
		{
			if (actorNode.Key != "Player")
				yield break;

			var hackyAIs = actorNode.ChildrenMatching("HackyAI", includeRemovals: false);
			if (!hackyAIs.Any())
				yield break;

			var addNodes = new List<MiniYamlNode>();

			// We add a 'default' HarvesterBotModule in any case (unless the file doesn't contain any HackyAI base definition),
			// and only add more for AIs that define custom values for one of its fields.
			var defaultHarvNode = new MiniYamlNode("HarvesterBotModule", "");
			var addDefaultHarvModule = false;

			foreach (var hackyAINode in hackyAIs)
			{
				// HackyAIInfo.Name might contain spaces, so Type is better suited to be used as condition name.
				// Type can be 'null' if the place we're updating is overriding the default rules (like a map's rules.yaml).
				// If that's the case, it's better to not perform most of the updates on this particular yaml file,
				// as most - or more likely all - necessary updates will already have been performed on the base ai yaml.
				var aiTypeNode = hackyAINode.LastChildMatching("Type");
				var aiType = aiTypeNode != null ? aiTypeNode.NodeValue<string>() : null;
				if (aiType == null)
				{
					locations.Add("{0} ({1})".F(hackyAINode.Key, hackyAINode.Location.Filename));
					continue;
				}

				addDefaultHarvModule = true;

				var conditionString = "enable-" + aiType + "-ai";
				var requiresCondition = new MiniYamlNode("RequiresCondition", conditionString);

				var addGrantConditionOnBotOwner = true;

				// Don't add GrantConditionOnBotOwner if it's already been added with matching condition
				var grantBotConditions = actorNode.ChildrenMatching("GrantConditionOnBotOwner");
				foreach (var grant in grantBotConditions)
					if (grant.LastChildMatching("Condition").NodeValue<string>() == conditionString)
						addGrantConditionOnBotOwner = false;

				if (addGrantConditionOnBotOwner)
				{
					var grantNode = new MiniYamlNode("GrantConditionOnBotOwner@" + aiType, "");
					var grantCondition = new MiniYamlNode("Condition", conditionString);
					var bot = new MiniYamlNode("Bots", aiType);
					grantNode.AddNode(grantCondition);
					grantNode.AddNode(bot);
					addNodes.Add(grantNode);
				}

				if (harvesterFields.Any(f => hackyAINode.ChildrenMatching(f).Any()))
				{
					var harvNode = new MiniYamlNode("HarvesterBotModule@" + aiType, "");
					harvNode.AddNode(requiresCondition);

					foreach (var hf in harvesterFields)
					{
						var fieldNode = hackyAINode.LastChildMatching(hf);
						if (fieldNode != null)
						{
							if (hf == "AssignRolesInterval")
								fieldNode.MoveAndRenameNode(hackyAINode, harvNode, "ScanForIdleHarvestersInterval");
							else
								fieldNode.MoveNode(hackyAINode, harvNode);
						}
					}

					addNodes.Add(harvNode);
				}
				else
				{
					// We want the default module to be enabled for every AI that didn't customise one of its fields,
					// so we need to update RequiresCondition to be enabled on any of the conditions granted by these AIs,
					// but only if the condition hasn't been added yet.
					var requiresConditionNode = defaultHarvNode.LastChildMatching("RequiresCondition");
					if (requiresConditionNode == null)
						defaultHarvNode.AddNode(requiresCondition);
					else
					{
						var oldValue = requiresConditionNode.NodeValue<string>();
						if (oldValue.Contains(conditionString))
							continue;

						requiresConditionNode.ReplaceValue(oldValue + " || " + conditionString);
					}
				}

				if (supportPowerFields.Any(f => hackyAINode.ChildrenMatching(f).Any()))
				{
					var spNode = new MiniYamlNode("SupportPowerBotModule@" + aiType, "");
					spNode.AddNode(requiresCondition);

					foreach (var spf in supportPowerFields)
					{
						var fieldNode = hackyAINode.LastChildMatching(spf);
						if (fieldNode != null)
							fieldNode.MoveAndRenameNode(hackyAINode, spNode, "Decisions");
					}

					addNodes.Add(spNode);
				}
			}

			if (addDefaultHarvModule)
				addNodes.Add(defaultHarvNode);

			foreach (var node in addNodes)
				actorNode.AddNode(node);

			yield break;
		}
	}
}
