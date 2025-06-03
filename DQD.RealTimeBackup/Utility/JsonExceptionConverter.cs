using System;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace DQD.RealTimeBackup.Utility;

public class JsonExceptionConverter : JsonConverterFactory
{
	public override bool CanConvert(Type typeToConvert)
		=> typeof(Exception).IsAssignableFrom(typeToConvert);

	public override JsonConverter? CreateConverter(Type typeToConvert, JsonSerializerOptions options)
	{
		if (!CanConvert(typeToConvert))
			return null;

		var type = typeof(JsonExceptionConverter<>).MakeGenericType(typeToConvert);

		return (JsonConverter?)Activator.CreateInstance(type, this);
	}
}

public class JsonExceptionConverter<T> : JsonConverter<T>
	where T : Exception
{
	JsonExceptionConverter _factory;

	public JsonExceptionConverter(JsonExceptionConverter factory)
	{
		_factory = factory;
	}

	public override T? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
		=> throw new NotImplementedException();

	public override void Write(Utf8JsonWriter writer, T value, JsonSerializerOptions options)
	{
		writer.WriteStartObject();

		writer.WriteString("Type", value.GetType().ToString());
		writer.WriteString("Message", value.Message);
		if (value.Source is string source)
			writer.WriteString("Source", source);
		if (value.StackTrace is string stackTrace)
			writer.WriteString("StackTrace", stackTrace);
		if (value.HResult != 0)
			writer.WriteString("HResult", value.HResult.ToString("X8"));
		if (value.HelpLink is string helpLink)
			writer.WriteString("HelpLink", helpLink);

		if ((value.Data != null) && (value.Data.Count > 0))
		{
			writer.WritePropertyName("Data");
			writer.WriteStartObject();

			foreach (System.Collections.DictionaryEntry keyValuePair in value.Data)
			{
				if (keyValuePair.Key?.ToString() is string key)
				{
					writer.WritePropertyName(key);

					if (keyValuePair.Value == null)
						writer.WriteNullValue();
					else
					{
						dynamic valueConverter = options.GetConverter(keyValuePair.Value!.GetType());

						valueConverter.Write(writer, keyValuePair.Value, options);
					}
				}
			}

			writer.WriteEndObject();
		}

		if (value.TargetSite != null)
		{
			string typeName = value.TargetSite.DeclaringType?.FullName ?? "<unknown>";
			string methodName = value.TargetSite.Name;
			string parameters = string.Join(
				", ",
				value.TargetSite.GetParameters().Select(p => p.ParameterType + " " + p.Name));

			writer.WriteString("TargetSite", $"[{typeName}]::{methodName}({parameters})");
		}

		if (value.InnerException != null)
		{
			dynamic innerExceptionConverter = options.GetConverter(value.InnerException.GetType());

			innerExceptionConverter.Write(writer, value.InnerException, options);
		}

		writer.WriteEndObject();
	}
}
