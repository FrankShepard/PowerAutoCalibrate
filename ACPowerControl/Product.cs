using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.IO.Ports;

namespace ACPowerControl
{
	public class Product
	{
		public decimal calibration_current_1;
		public decimal calibration_current_2;
		public decimal calibration_current_3;

		public decimal desinged_ocp_1;
		public decimal desinged_ocp_2;
		public decimal desinged_ocp_3;

		public int output_channel;

		public bool mainpower_undervoltage_should_calibrate;    //主电欠压点是否需要进行测试的标志
		public bool mainpower_voltage_should_calirate;	//主电电压是否需要进行测试的标志

		public int communicate_baudrate;
		public SpeciesName species_name;
		/// <summary>
		/// 产品通讯使用的串口校验类型
		/// </summary>
		public Parity serial_parity;

		/// <summary>
		/// 若是后续新增产品种类，则需要继续扩展
		/// </summary>
		public string[] Species = {
			"IG-B1031F/IG-B03S01","IG-B1032F","IG-B1061F/IG-B06S01","IG-B1061H","IG-B2022F","IG-B2031F/IG-B03D01","IG-B2031G/IG-B03D02","IG-B2031H/IG-B03D03","IG-B2032F","IG-B2032H","IG-B1051H","IG-B2053F","IG-B2053H","IG-B2053K","IG-B2073F",
			"IG-M1101F/IG-M10S01","IG-M1101H","IG-M1102F","IG-M1202F","IG-M1302F","IG-M1102H","IG-M1202H", "J-EI8212","IG-M2102F","GST-LD-D02H","IG-M2121F","IG-B2108","IG-M2131H","IG-M2132F","IG-M2202F","IG-M3201F/20A主机电源",
			"GST-LD-D06H" ,"IG-M3202F","IG-M3242F","IG-M3302F",	"IG-X1032F" ,"IG-X1041F","IG-X1061F","IG-X1101F","IG-X1101H","IG-X1101K","IG-X1201F","IG-X1301F","IG-Z2071F","IG-Z2102F","IG-Z2121F","IG-Z2181F","IG-Z2182F","IG-Z2272F","IG-Z2102L",
			"IG-Z2182L" ,"IG-Z2272L",
		};

		/// <summary>
		///  若是后续新增产品种类，则需要继续扩展;必须与
		/// </summary>
		public enum SpeciesName
		{
			IG_B1031F__IG_B03S01 = 0,
			IG_B1032F,
			IG_B1061F__IG_B06S01,
			IG_B1061H,		
			IG_B2022F,
			IG_B2031F__IG_B03D01,
			IG_B2031G__IG_B03D02,
			IG_B2031H__IG_B03D03,
			IG_B2032F,
			IG_B2032H,
			IG_B1051H,
			IG_B2053F,
			IG_B2053H,
			IG_B2053K,
			IG_B2073F,
			IG_M1101F__IG_M10S01,
			IG_M1101H,
			IG_M1102F,
			IG_M1202F,
			IG_M1302F,
			IG_M1102H,
			IG_M1202H,
			J_EI8212,			
			IG_M2102F,
			GST_LD_D02H,
			IG_M2121F,
			IG_B2108,
			IG_M2131H,
			IG_M2132F,
			IG_M2202F,
			IG_M3201F__20A主机电源,
			GST_LD_D06H,
			IG_M3202F,
			IG_M3242F,
			IG_M3302F,
			IG_X1032F,
			IG_X1041F,
			IG_X1061F,
			IG_X1101F,
			IG_X1101H,
			IG_X1101K,
			IG_X1201F,
			IG_X1301F,
			IG_Z2071F,
			IG_Z2102F,
			IG_Z2121F,
			IG_Z2181F,
			IG_Z2182F,
			IG_Z2272F,
			IG_Z2102L,
			IG_Z2182L,
			IG_Z2272L,
		};
	}
}
