﻿using System.Linq;
using System.Windows.Forms;
using Vixen.Data.Flow;
using Vixen.Module;
using Vixen.Module.OutputFilter;
using VixenModules.OutputFilter.Color.Filter;

namespace VixenModules.OutputFilter.Color {
	public class ColorModule : OutputFilterModuleInstanceBase {
		private ColorData _data;
		private ColorOutput[] _outputs;

		public override void Handle(IntentsDataFlowData obj) {
			foreach(ColorOutput output in Outputs) {
				output.ProcessInputData(obj);
			}
		}

		public override DataFlowType InputDataType {
			get { return DataFlowType.MultipleIntents; }
		}

		public override DataFlowType OutputDataType {
			get { return DataFlowType.MultipleIntents; }
		}

		public override IDataFlowOutput[] Outputs {
			get { return _outputs; }
		}

		public override IModuleDataModel ModuleData {
			get { return _data; }
			set { 
				_data = (ColorData)value;
				_CreateOutputs();
			}
		}

		public override bool HasSetup {
			get { return true; }
		}

		public override bool Setup() {
			using(ColorSetupForm colorSetupForm = new ColorSetupForm(_data)) {
				if(colorSetupForm.ShowDialog() == DialogResult.OK) {
					_data.FilterOrder = colorSetupForm.SelectedFilters;
					_CreateOutputs();
					return true;
				}
			}
			return false;
		}

		private ColorComponentFilter _CreateFilter(ColorFilter colorFilter) {
			switch(colorFilter) {
				case ColorFilter.Red:
					return new RedFilter();
				case ColorFilter.Green:
					return new GreenFilter();
				case ColorFilter.Blue:
					return new BlueFilter();
				case ColorFilter.Yellow:
					return new YellowFilter();
				case ColorFilter.White:
					return new WhiteFilter();
				default:
					return new NoFilter();
			}
		}

		private void _CreateOutputs() {
			_outputs = _data.FilterOrder.Select(x => new ColorOutput(_CreateFilter(x))).ToArray();
		}
	}
}
