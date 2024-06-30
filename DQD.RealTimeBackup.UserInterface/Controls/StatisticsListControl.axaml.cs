using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Reflection;
using System.Text;

using Avalonia;
using Avalonia.Controls;

using DQD.RealTimeBackup.Agent;
using DQD.RealTimeBackup.Bridge.Messages;
using DQD.RealTimeBackup.Bridge.Serialization;
using DQD.RealTimeBackup.Scan;
using DQD.RealTimeBackup.Utility;

namespace DQD.RealTimeBackup.UserInterface.Controls
{
	public partial class StatisticsListControl : UserControl, INotifyPropertyChanged
	{
		public static readonly StyledProperty<object?> StatisticsObjectProperty =
			AvaloniaProperty.Register<StatisticsListControl, object?>(nameof(StatisticsObject));

		public object? StatisticsObject
		{
			get => GetValue(StatisticsObjectProperty);
			set => SetValue(StatisticsObjectProperty, value);
		}

		public StatisticsListControl()
		{
			InitializeComponent();

			PropertyChanged +=
				(sender, e) =>
				{
					if (e.Property == StatisticsObjectProperty)
					{
						StatisticsListControl_StatisticsObjectChanged(sender, e);
					}
				};
		}

		public void ConfigureForGetStatsResponse()
		{
			ClearFieldBindings();

			AddFieldBindingsForType<BridgeMessage_GetStats_Response, BackupAgentQueueSizes>(response => response.BackupAgentQueueSizes);
			AddFieldBindingsForType<BridgeMessage_GetStats_Response>();
		}

		public void ConfigureForRescanStatus(bool includeBackupAgentQueueSizes)
		{
			ClearFieldBindings();

			bool RescanStatusFilter(FieldInfo fieldInfo) =>
				fieldInfo.Name == nameof(RescanStatus.NumberOfFilesLeftToMatch);
			bool ScanStatusFilter(FieldInfo fieldInfo) =>
				fieldInfo.Name != nameof(ScanStatus.ZFSSnapshotCount);

			AddFieldBindingsForType<RescanStatus, ScanStatus>(response => response, ScanStatusFilter);
			AddFieldBindingsForType<RescanStatus>(RescanStatusFilter);

			if (includeBackupAgentQueueSizes)
			{
				bool IncludeZFSSnapshotCountFilter(FieldInfo fieldInfo) =>
					fieldInfo.Name == nameof(ScanStatus.ZFSSnapshotCount);

				AddFieldBindingsForType<ScanStatus, BackupAgentQueueSizes>(response => response.BackupAgentQueueSizes);
				AddFieldBindingsForType<ScanStatus>(IncludeZFSSnapshotCountFilter);
			}

			var zfsFieldIndex = _fieldBindings.Count - 1;
		}

		class FieldBinding
		{
			public FieldInfo FieldInfo;
			public Label Label;
			public Action<object>? Apply;

			public FieldBinding(FieldInfo fieldInfo, Label label)
			{
				FieldInfo = fieldInfo;
				Label = label;
			}

			public FieldBinding(FieldInfo fieldInfo, Label label, Action<object> apply)
			{
				FieldInfo = fieldInfo;
				Label = label;
				Apply = apply;
			}
		}

		List<FieldBinding> _fieldBindings = new List<FieldBinding>();

		void ClearFieldBindings()
		{
			_fieldBindings.Clear();
		}

		void AddFieldBindingsForType<TObject>(Func<FieldInfo, bool>? filter = null)
		{
			List<(FieldInfo FieldInfo, int Order)> fields = new List<(FieldInfo FieldInfo, int Order)>();

			foreach (var fieldInfo in typeof(TObject).GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.DeclaredOnly))
			{
				if (fieldInfo.FieldType.IsClass && (fieldInfo.FieldType != typeof(string)))
					continue;
				if ((filter != null) && !filter(fieldInfo))
					continue;

				int fieldOrder = int.MaxValue;

				if (fieldInfo.GetCustomAttribute<FieldOrderAttribute>() is FieldOrderAttribute fieldOrderAttribute)
					fieldOrder = fieldOrderAttribute.Order;

				fields.Add((fieldInfo, fieldOrder));
			}

			fields.Sort((a, b) => a.Order.CompareTo(b.Order));

			foreach (var field in fields)
			{
				var label = AddLineForField(field.FieldInfo);

				_fieldBindings.Add(new FieldBinding(
					field.FieldInfo,
					label));
			}
		}

		void AddFieldBindingsForType<TObject, TSubject>(Func<TObject, TSubject?> resolveSubject, Func<FieldInfo, bool>? filter = null)
		{
			object cacheKey = default!;
			TSubject? cachedSubject = default;

			TSubject? GetResolvedSubject(object obj)
			{
				if (!ReferenceEquals(obj, cacheKey))
				{
					if (obj is TObject typedObject)
						cachedSubject = resolveSubject(typedObject);
					else
						cachedSubject = default;

					cacheKey = obj;
				}

				return cachedSubject;
			}

			List<(FieldInfo FieldInfo, int Order)> fields = new List<(FieldInfo FieldInfo, int Order)>();

			foreach (var fieldInfo in typeof(TSubject).GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.DeclaredOnly))
			{
				if (fieldInfo.FieldType.IsClass && (fieldInfo.FieldType != typeof(string)))
					continue;
				if ((filter != null) && !filter(fieldInfo))
					continue;

				int fieldOrder = int.MaxValue;

				if (fieldInfo.GetCustomAttribute<FieldOrderAttribute>() is FieldOrderAttribute fieldOrderAttribute)
					fieldOrder = fieldOrderAttribute.Order;

				fields.Add((fieldInfo, fieldOrder));
			}

			fields.Sort((a, b) => a.Order.CompareTo(b.Order));

			foreach (var field in fields)
			{
				var label = AddLineForField(field.FieldInfo);

				void Apply(object obj)
				{
					label.Content = string.Format("{0:#,##0}", field.FieldInfo.GetValue(GetResolvedSubject(obj)));
				}

				_fieldBindings.Add(new FieldBinding(
					field.FieldInfo,
					label,
					Apply));
			}
		}

		Label AddLineForField(FieldInfo fieldInfo)
		{
			var lblHeading = new Label();
			var lblValue = new Label();

			if (fieldInfo.GetCustomAttribute<UIHeadingAttribute>() is UIHeadingAttribute uiHeadingAttribute)
				lblHeading.Content = uiHeadingAttribute.Text;
			else
				lblHeading.Content = InferHeadingFromFieldName(fieldInfo.Name);

			lblHeading.Margin = new Thickness(0, 0, 30, 0);

			lblValue.HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Right;

			int rowIndex = grdTable.RowDefinitions.Count;

			grdTable.RowDefinitions.Add(new RowDefinition(GridLength.Auto));

			Grid.SetRow(lblHeading, rowIndex);
			Grid.SetColumn(lblHeading, 0);

			Grid.SetRow(lblValue, rowIndex);
			Grid.SetColumn(lblValue, 1);

			grdTable.Children.Add(lblHeading);
			grdTable.Children.Add(lblValue);

			return lblValue;
		}

		internal static string InferHeadingFromFieldName(string name)
		{
			var builder = new StringBuilder();

			for (int i=0; i < name.Length; i++)
			{
				void ProcessAcronym()
				{
					int acronymStart = builder.Length;

					while ((i < name.Length)
				      && ((i + 1 >= name.Length) || (char.IsUpper(name, i + 1) || (name[i + 1] == '_'))))
					{
						builder!.Append(name[i]);
						i++;
					}

					if (builder!.Length == acronymStart + 1)
						builder[acronymStart] = char.ToLower(builder[acronymStart]);

					if (i < name.Length)
						i--;
				}

				void ProcessStartOfWord()
				{
					if ((i + 1 == name.Length) || char.IsUpper(name, i + 1))
						ProcessAcronym();
					else
						builder.Append(char.ToLower(name[i]));
				}

				if (name[i] == '_')
				{
					builder.Append(' ');

					if ((i + 1 < name.Length) && char.IsUpper(name, i + 1))
					{
						// Upper-case letter following an underscore. Is an acronym?
						i++;
						ProcessStartOfWord();
					}
				}
				else if ((i == 0) || char.IsUpper(name, i))
				{
					// Start of a new word.
					if (i > 0)
						builder.Append(' ');

					ProcessStartOfWord();

					if (i == 0)
						builder[0] = char.ToUpper(builder[0]);
				}
				else
					builder.Append(name[i]);
			}

			return builder.ToString();
		}

		void StatisticsListControl_StatisticsObjectChanged(object? sender, AvaloniaPropertyChangedEventArgs e)
		{
			var newObject = e.NewValue;

			if (newObject == null)
			{
				foreach (var binding in _fieldBindings)
					binding.Label.Content = "";
			}
			else
			{
				foreach (var binding in _fieldBindings)
				{
					if (binding.Apply != null)
						binding.Apply(newObject);
					else
						binding.Label.Content = string.Format("{0:#,##0}", binding.FieldInfo.GetValue(newObject));
				}
			}
		}
	}
}
