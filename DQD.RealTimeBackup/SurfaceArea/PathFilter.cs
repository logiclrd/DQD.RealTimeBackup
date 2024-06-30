using System;
using System.Linq;
using System.Xml;
using System.Xml.Schema;
using System.Xml.Serialization;
using System.Text.RegularExpressions;

using DQD.RealTimeBackup.Interop;

namespace DQD.RealTimeBackup.SurfaceArea
{
	public class PathFilter : IXmlSerializable
	{
		PathFilterType _type;
		string _value;
		bool _shouldExclude;

		public string Value => _value;
		public bool ShouldExclude => _shouldExclude;

		public static IUserList UserList = new UserList(new PasswdProvider());

		public PathFilter()
		{
			_value = "uninitialized$";
		}

		public PathFilter(PathFilterType type, string value, bool shouldExclude)
		{
			_type = type;
			_value = value;
			_shouldExclude = shouldExclude;
		}

		XmlSchema? IXmlSerializable.GetSchema() => null;

		void IXmlSerializable.WriteXml(XmlWriter writer)
		{
			// Our caller leaves us immediately after the start element that represents
			// where this instance is in the object graph.
			writer.WriteAttributeString("Type", _type.ToString());
			writer.WriteAttributeString("Value", _value);
			writer.WriteAttributeString("Action", _shouldExclude ? "Exclude" : "Include");
		}

		void IXmlSerializable.ReadXml(XmlReader reader)
		{
			// Our caller has _not_ read the element that was already started when
			// WriteXml was called. That's up to us to read.
			reader.MoveToContent();

			_type = (PathFilterType)Enum.Parse(typeof(PathFilterType), reader.GetAttribute("Type") ?? "Unknown");
			_value = reader.GetAttribute("Value") ?? "uninitialized$";
			_shouldExclude = reader.GetAttribute("Action") == "Exclude";

			if (reader.IsEmptyElement)
				reader.ReadStartElement();
			else
			{
				reader.ReadStartElement();
				reader.ReadEndElement();
			}
		}

		Regex? _compiledRegex;

		public bool Matches(string path)
		{
			_compiledRegex ??= GenerateRegularExpression();

			return _compiledRegex.IsMatch(path);
		}

		internal Regex GenerateRegularExpression()
		{
			string expression;

			switch (_type)
			{
				case PathFilterType.Prefix:
					expression = "^" + Regex.Escape(_value) + "(/|$)";
					break;
				case PathFilterType.Component:
					expression = "/" + Regex.Escape(_value) + "(/|$)";
					break;
				case PathFilterType.RegularExpression:
					expression = _value;

					if (expression.StartsWith("~"))
					{
						var homePaths = UserList.EnumerateRealUsers().Select(user => user.HomePath.TrimEnd('/'));

						string homePathsSubexpressioon = "(" + string.Join("|", homePaths) + ")";

						expression = homePathsSubexpressioon + "/" + expression.Substring(1).TrimStart('/');
					}

					break;

				default:
					throw new Exception("Improperly initialized PathFilter: Unrecognized type value " + _type);
			}

			return new Regex(expression, RegexOptions.Compiled | RegexOptions.Singleline);
		}

		public static PathFilter ForPrefix(string prefix, bool shouldExclude)
			=> new PathFilter(PathFilterType.Prefix, prefix, shouldExclude);

		public static PathFilter IncludePrefix(string prefix)
			=> ForPrefix(prefix, shouldExclude: false);
		public static PathFilter ExcludePrefix(string prefix)
			=> ForPrefix(prefix, shouldExclude: true);

		public static PathFilter ForComponent(string component, bool shouldExclude)
			=> new PathFilter(PathFilterType.Component, component, shouldExclude);

		public static PathFilter IncludeComponent(string component)
			=> ForComponent(component, shouldExclude: false);
		public static PathFilter ExcludeComponent(string component)
			=> ForComponent(component, shouldExclude: true);

		public static PathFilter IncludeRegex(string regex)
			=> new PathFilter(PathFilterType.RegularExpression, regex, shouldExclude: false);
		public static PathFilter ExcludeRegex(string regex)
			=> new PathFilter(PathFilterType.RegularExpression, regex, shouldExclude: true);
	}
}
