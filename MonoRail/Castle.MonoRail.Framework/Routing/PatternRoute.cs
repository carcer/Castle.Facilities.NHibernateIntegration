﻿// Copyright 2004-2007 Castle Project - http://www.castleproject.org/
// 
// Licensed under the Apache License, Version 2.0 (the "License");
// you may not use this file except in compliance with the License.
// You may obtain a copy of the License at
// 
//     http://www.apache.org/licenses/LICENSE-2.0
// 
// Unless required by applicable law or agreed to in writing, software
// distributed under the License is distributed on an "AS IS" BASIS,
// WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
// See the License for the specific language governing permissions and
// limitations under the License.

namespace Castle.MonoRail.Framework.Routing
{
	using System;
	using System.Collections;
	using System.Collections.Generic;
	using System.Diagnostics;
	using System.Text;
	using System.Text.RegularExpressions;
	using Castle.MonoRail.Framework.Services.Utils;
	using Descriptors;

	/// <summary>
	/// Pendent
	/// </summary>
	[DebuggerDisplay("PatternRoute {pattern}")]
	public class PatternRoute : IRoutingRule
	{
		private readonly string pattern;
		private readonly List<DefaultNode> nodes = new List<DefaultNode>();
		private readonly Dictionary<string, string> defaults = new Dictionary<string, string>(StringComparer.InvariantCultureIgnoreCase);

		/// <summary>
		/// Initializes a new instance of the <see cref="PatternRoute"/> class.
		/// </summary>
		/// <param name="pattern">The pattern.</param>
		public PatternRoute(string pattern)
		{
			this.pattern = pattern;
			CreatePatternNodes();
		}

		/// <summary>
		/// Gets the name of the route.
		/// </summary>
		/// <value>The name of the route.</value>
		public string RouteName
		{
			get { return null; }
		}

		/// <summary>
		/// Pendent
		/// </summary>
		/// <param name="hostname">The hostname.</param>
		/// <param name="virtualPath">The virtual path.</param>
		/// <param name="parameters">The parameters.</param>
		/// <returns></returns>
		public string CreateUrl(string hostname, string virtualPath, IDictionary parameters)
		{
			StringBuilder text = new StringBuilder(virtualPath);
			bool hasNamed = false;

			foreach(DefaultNode node in nodes)
			{
				AppendSlashOrDot(text, node);

				if (node.name == null)
				{
					text.Append(node.start);
				}
				else
				{
					hasNamed = true;

					object value = parameters[node.name];

					if (value == null)
					{
						if (!node.optional)
						{
							return null;
						}
						else
						{
							break;
						}
					}
					else
					{
						if (node.hasRestriction && !node.Accepts(value.ToString()))
						{
							return null;
						}

						if (node.optional && StringComparer.InvariantCultureIgnoreCase.Compare(node.DefaultVal, value.ToString()) == 0)
						{
							break; // end as there can't be more required nodes after an optional one
						}

						text.Append(value.ToString());
					}
				}
			}

			if (text.Length == 0 || text[text.Length - 1] == '/')
			{
				text.Length = text.Length - 1;
			}

			return hasNamed ? text.ToString() : null;
		}

		/// <summary>
		/// Determines if the specified URL matches the
		/// routing rule.
		/// </summary>
		/// <param name="url">The URL.</param>
		/// <param name="context">The context</param>
		/// <param name="match">The match.</param>
		/// <returns></returns>
		public bool Matches(string url, IRouteContext context, RouteMatch match)
		{
			string[] parts = url.Split(new char[] {'/', '.'}, StringSplitOptions.RemoveEmptyEntries);
			int index = 0;

			foreach(DefaultNode node in nodes)
			{
				string part = index < parts.Length ? parts[index] : null;

				if (!node.Matches(part, match))
				{
					return false;
				}

				index++;
			}

			foreach(KeyValuePair<string, string> pair in defaults)
			{
				if (!match.Parameters.ContainsKey(pair.Key))
				{
					match.Parameters.Add(pair.Key, pair.Value);
				}
			}

			return true;
		}

		private void CreatePatternNodes()
		{
			string[] parts = pattern.Split(new char[] {'/'}, StringSplitOptions.RemoveEmptyEntries);

			foreach(string part in parts)
			{
				string[] subparts = part.Split(new char[] { '.' }, 2, StringSplitOptions.RemoveEmptyEntries);

				if (subparts.Length == 2)
				{
					bool afterDot = false;

					foreach(string subpart in subparts)
					{
						if (subpart.Contains("["))
						{
							nodes.Add(CreateNamedOptionalNode(subpart, afterDot));
						}
						else
						{
							nodes.Add(CreateRequiredNode(subpart, afterDot));
						}

						afterDot = true;
					}
				}
				else
				{
					if (part.Contains("["))
					{
						nodes.Add(CreateNamedOptionalNode(part, false));
					}
					else
					{
						nodes.Add(CreateRequiredNode(part, false));
					}
				}
			}
		}

		/// <summary>
		/// Adds a default entry.
		/// </summary>
		/// <param name="key">The key.</param>
		/// <param name="value">The value.</param>
		public void AddDefault(string key, string value)
		{
			defaults[key] = value;
		}

		private DefaultNode CreateNamedOptionalNode(string part, bool afterDot)
		{
			return new DefaultNode(part, true, afterDot);
		}

		private DefaultNode CreateRequiredNode(string part, bool afterDot)
		{
			return new DefaultNode(part, false, afterDot);
		}

		private static void AppendSlashOrDot(StringBuilder text, DefaultNode node)
		{
			if (text.Length == 0 || text[text.Length - 1] != '/')
			{
				if (node.afterDot)
				{
					text.Append('.');
				}
				else
				{
					text.Append('/');
				}
			}
		}

		#region DefaultNode

		[DebuggerDisplay("Node {name} Opt: {optional} default: {defaultVal} Regular exp: {exp}")]
		private class DefaultNode
		{
			public readonly string name, start, end;
			public readonly bool optional;
			public readonly bool afterDot;
			public bool hasRestriction;
			private string defaultVal;
			private bool acceptsIntOnly;
			private string[] acceptedTokens;
			private Regex exp;

			public DefaultNode(string part, bool optional, bool afterDot)
			{
				this.optional = optional;
				this.afterDot = afterDot;
				int indexStart = part.IndexOfAny(new char[] {'<', '['});
				int indexEndStart = -1;

				if (indexStart != -1)
				{
					indexEndStart = part.IndexOfAny(new char[] {'>', ']'}, indexStart);
					name = part.Substring(indexStart + 1, indexEndStart - indexStart - 1);
				}

				if (indexStart != -1)
				{
					start = part.Substring(0, indexStart);
				}
				else
				{
					start = part;
				}

				end = "";

				if (indexEndStart != -1)
				{
					end = part.Substring(indexEndStart + 1);
				}

				ReBuildRegularExpression();
			}

			private void ReBuildRegularExpression()
			{
				RegexOptions options = RegexOptions.Compiled | RegexOptions.Singleline;

				if (name != null)
				{
					exp = new Regex("^" + CharClass(start) + "(" + GetExpression() + ")" + CharClass(end) + "$", options);
				}
				else
				{
					exp = new Regex("^(" + CharClass(start) + ")$");
				}
			}

			private string GetExpression()
			{
				if (acceptsIntOnly)
				{
					return "[0-9]+";
				}
				else if (acceptedTokens != null && acceptedTokens.Length != 0)
				{
					StringBuilder text = new StringBuilder();

					foreach(string token in acceptedTokens)
					{
						if (text.Length != 0)
						{
							text.Append("|");
						}
						text.Append("(");
						text.Append(CharClass(token));
						text.Append(")");
					}

					return text.ToString();
				}
				else
				{
					return "[a-zA-Z,_,0-9,-]+";
				}
			}

			public bool Matches(string part, RouteMatch match)
			{
				if (part == null)
				{
					if (optional)
					{
						if (name != null)
						{
							match.AddNamed(name, defaultVal);
						}
						return true;
					}
					else
					{
						return false;
					}
				}

				Match regExpMatch = exp.Match(part);

				if (regExpMatch.Success)
				{
					if (name != null)
					{
						match.AddNamed(name, part);
					}

					return true;
				}

				return false;
			}

			public void AcceptsAnyOf(string[] names)
			{
				hasRestriction = true;
				acceptedTokens = names;
				ReBuildRegularExpression();
			}

			public string DefaultVal
			{
				get { return defaultVal; }
				set { defaultVal = value; }
			}

			public bool AcceptsIntOnly
			{
				set
				{
					hasRestriction = true;
					acceptsIntOnly = value;
					ReBuildRegularExpression();
				}
			}

			public bool Accepts(string val)
			{
				Match regExpMatch = exp.Match(val);

				return (regExpMatch.Success);
			}
		}

		#endregion

		/// <summary>
		/// Configures the default for the named pattern part.
		/// </summary>
		/// <param name="namedPatternPart">The named pattern part.</param>
		/// <returns></returns>
		public DefaultConfigurer DefaultFor(string namedPatternPart)
		{
			return new DefaultConfigurer(this, namedPatternPart);
		}

		/// <summary>
		/// Configures the default for the named pattern part.
		/// </summary>
		/// <returns></returns>
		public DefaultConfigurer DefaultForController()
		{
			return new DefaultConfigurer(this, "controller");
		}

		/// <summary>
		/// Configures the default for the named pattern part.
		/// </summary>
		/// <returns></returns>
		public DefaultConfigurer DefaultForAction()
		{
			return new DefaultConfigurer(this, "action");
		}

		/// <summary>
		/// Configures the default for the named pattern part.
		/// </summary>
		/// <returns></returns>
		public DefaultConfigurer DefaultForArea()
		{
			return new DefaultConfigurer(this, "area");
		}

		/// <summary>
		/// Configures restrictions for the named pattern part.
		/// </summary>
		/// <param name="namedPatternPart">The named pattern part.</param>
		/// <returns></returns>
		public RestrictionConfigurer Restrict(string namedPatternPart)
		{
			return new RestrictionConfigurer(this, namedPatternPart);
		}

		/// <summary>
		/// Pendent
		/// </summary>
		public class RestrictionConfigurer
		{
			private readonly PatternRoute route;
			private readonly DefaultNode targetNode;

			/// <summary>
			/// Initializes a new instance of the <see cref="RestrictionConfigurer"/> class.
			/// </summary>
			/// <param name="route">The route.</param>
			/// <param name="namedPatternPart">The named pattern part.</param>
			public RestrictionConfigurer(PatternRoute route, string namedPatternPart)
			{
				this.route = route;
				targetNode = route.GetNamedNode(namedPatternPart, true);
			}

			/// <summary>
			/// Anies the of.
			/// </summary>
			/// <param name="validNames">The valid names.</param>
			/// <returns></returns>
			public PatternRoute AnyOf(params string[] validNames)
			{
				targetNode.AcceptsAnyOf(validNames);
				return route;
			}

			/// <summary>
			/// Gets the valid integer.
			/// </summary>
			/// <value>The valid integer.</value>
			public PatternRoute ValidInteger
			{
				get
				{
					targetNode.AcceptsIntOnly = true;
					return route;
				}
			}
		}

		/// <summary>
		/// Pendent
		/// </summary>
		public class DefaultConfigurer
		{
			private readonly PatternRoute route;
			private readonly string namedPatternPart;
			private readonly DefaultNode targetNode;

			/// <summary>
			/// Initializes a new instance of the <see cref="DefaultConfigurer"/> class.
			/// </summary>
			/// <param name="patternRoute">The pattern route.</param>
			/// <param name="namedPatternPart">The named pattern part.</param>
			public DefaultConfigurer(PatternRoute patternRoute, string namedPatternPart)
			{
				route = patternRoute;
				this.namedPatternPart = namedPatternPart;
				targetNode = route.GetNamedNode(namedPatternPart, false);
			}

			/// <summary>
			/// Sets the default value for this named pattern part.
			/// </summary>
			/// <returns></returns>
			public PatternRoute Is<T>() where T : class, IController
			{
				ControllerDescriptor desc = ControllerInspectionUtil.Inspect(typeof(T));
				if (targetNode != null)
				{
					targetNode.DefaultVal = desc.Name;
				}
				route.AddDefault(namedPatternPart, desc.Name);
				return route;
			}

			/// <summary>
			/// Sets the default value for this named pattern part.
			/// </summary>
			/// <param name="value">The value.</param>
			/// <returns></returns>
			public PatternRoute Is(string value)
			{
				if (targetNode != null)
				{
					targetNode.DefaultVal = value;
				}
				route.AddDefault(namedPatternPart, value);
				return route;
			}

			/// <summary>
			/// Sets the default value as empty for this named pattern part.
			/// </summary>
			/// <value>The is empty.</value>
			public PatternRoute IsEmpty
			{
				get { return Is(string.Empty); }
			}
		}

		// See http://weblogs.asp.net/justin_rogers/archive/2004/03/20/93379.aspx
		private static string CharClass(string content)
		{
			if (content == String.Empty)
			{
				return string.Empty;
			}

			StringBuilder builder = new StringBuilder();

			foreach(char c in content)
			{
				if (char.IsLetter(c))
				{
					builder.AppendFormat("[{0}{1}]", char.ToLower(c), char.ToUpper(c));
				}
				else
				{
					builder.Append(c);
				}
			}

			return builder.ToString();
		}

		/// <summary>
		/// Gets the named node.
		/// </summary>
		/// <param name="part">The part.</param>
		/// <param name="mustFind">if set to <c>true</c> [must find].</param>
		/// <returns></returns>
		private DefaultNode GetNamedNode(string part, bool mustFind)
		{
			DefaultNode found = nodes.Find(delegate(DefaultNode node) { return node.name == part; });

			if (found == null && mustFind)
			{
				throw new ArgumentException("Could not find pattern node for name " + part);
			}

			return found;
		}
	}
}