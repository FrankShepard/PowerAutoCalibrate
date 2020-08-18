using System;
using System.IO.Ports;
using System.Threading;

namespace ACPowerControl
{
	/// <summary>
	/// 定义管理员账户通讯的操作方式
	/// </summary>
	public class MCU_Control : IDisposable
	{
		#region -- 与单片机通讯需要使用的枚举的说明 

		/// <summary>
		/// 用于上位机于单片机程序之间的命令码，协议中的第2个字节
		/// </summary>
		public enum Command : byte
		{
			/// <summary>
			/// 向单片机中设置
			/// </summary>
			Set = 0xAA,
			/// <summary>
			/// 单片机软复位
			/// </summary>
			Reset = 0xBB,
		};

		#region -- PC与单片机之间的通讯代码指令(Config)

		/// <summary>
		/// 进行通讯的Config码
		/// </summary>
		public enum Config : byte
		{
			/// <summary>
			/// 默认，无意义
			/// </summary>
			Default_No_Sense = 0x00,
			/// <summary>
			/// Write : 输出通道1设定输出电压，用于后续扩展
			/// </summary>
			OutputTargetSet_1 = 0x40,
			/// <summary>
			/// Write : 输出通道2设定输出电压，用于后续扩展
			/// </summary>
			OutputTargetSet_2 = 0x41,
			/// <summary>
			/// Write : 输出通道3设定输出电压，用于后续扩展
			/// </summary>
			OutputTargetSet_3 = 0x42,
			/// <summary>
			/// Write : 输出通道1的零点校准
			/// </summary>
			ZeroCalibrate_1 = 0x43,
			/// <summary>
			/// Write : 输出通道2的零点校准
			/// </summary>
			ZeroCalibrate_2 = 0x44,
			/// <summary>
			/// Write : 输出通道3的零点校准
			/// </summary>
			ZeroCalibrate_3 = 0x45,
			/// <summary>
			/// Write : 设置输出通道1的软件过流点
			/// </summary>
			OverCurrentSet_1 = 0x46,
			/// <summary>
			/// Write : 设置输出通道2的软件过流点
			/// </summary>
			OverCurrentSet_2 = 0x47,
			/// <summary>
			/// Write : 设置输出通道3的软件过流点
			/// </summary>
			OverCurrentSet_3 = 0x48,
			/// <summary>
			/// Write : 输出通道1的电压与电流校准
			/// </summary>
			DisplayVoltage_Ratio_1 = 0x49,
			/// <summary>
			/// Write : 输出通道2的电压与电流校准
			/// </summary>
			DisplayVoltage_Ratio_2 = 0x4A,
			/// <summary>
			/// Write : 输出通道3的电压与电流校准
			/// </summary>
			DisplayVoltage_Ratio_3 = 0x4B,
			/// <summary>
			/// Write : 输出电压的调整(硬件设计问题，所有通道的电压时调整，待后续扩展)
			/// </summary>
			OutputVoltage_Adjust = 0x4C,
			/// <summary>
			/// 主电校准时的频率计数获取，50Hz时计数值约为200
			/// </summary>
			Mcu_MainpowerPeriodCountGet = 0x4D,
			/// <summary>
			/// 主电欠压点时刻主电S1SL信号的低电平计数值
			/// </summary>
			Mcu_MainpowerUnderVoltageCountGet = 0x4E,
			/// <summary>
			/// 备电电压的显示系数的获取 - 表示总的备电电压
			/// </summary>
			DisplayVoltage_Ratio_Bat = 0x4F,
			/// <summary>
			/// 主电快要过压时停止充电时刻S1SL信号的低电平计数值
			/// </summary>
			Mcu_CannotChargeHighCountGet = 0x50,
			/// <summary>
			/// 主电电压校准
			/// </summary>
			MainpowerVoltageCalibrate = 0x51,
			/// <summary>
			/// 备电时的输出1电流与主电时电流的系数
			/// </summary>
			RatioSpCurrentToMp_1 = 0x52,
			/// <summary>
			/// 管理员指令，对特定的产品而言，使用本指令来保证始终处于充电的状态（应急照明电源）
			/// </summary>
			AlwaysCharging = 0x53,
			/// <summary>
			/// 管理员指令，对特定产品，使用本指令之后会造成备电单投功能无效（应急照明电源）
			/// </summary>
			BatsSingleWorkDisable = 0x54,
			/// <summary>
			/// 备电时的输出2电流与主电时电流的系数
			/// </summary>
			RatioSpCurrentToMp_2 = 0x55,
			/// <summary>
			/// 备电时的输出3电流与主电时电流的系数
			/// </summary>
			RatioSpCurrentToMp_3 = 0x56,
			/// <summary>
			/// 清除单片机中相应的扇区中的校准数据
			/// </summary>
			Mcu_ClearValidationCode = 0x58,
			/// <summary>
			/// 更改蜂鸣器使用标准的时长
			/// </summary>
			ChangeBeepWorkingTime = 0x59,
			/// <summary>
			/// 更改产品被校准后的标志位
			/// </summary>
			SetBeValidatedFlag = 0x5A,
			/// <summary>
			/// 设置产品类型-用于同一系列下属的多种型号
			/// </summary>
			SetProductModel = 0x5B,
			/// <summary>
			/// 设置单片机内指定地址的Flash数据
			/// </summary>
			FlashDataSet = 0x70,
			/// <summary>
			/// 读取单片机内指定地址的Flash数据
			/// </summary>
			FlashDataRead = 0x71,
		}

		#endregion

		#endregion

		#region -- 与单片机进行通讯的具体方法 

		#region -- 全局变量的声明 

		/*通讯代码长度*/
		const byte Communicate_Code_Length = 9;
		/*单片机通讯同步头*/
		const byte Header = 0x77;
		/*单片机通讯同步尾*/
		const byte End = 0x33;
		/*单片机通讯应答标志*/
		const byte Check = 0x1F;
		/*用于存放下位机通过串口返回的数据*/
		static byte[] Serialport_Redata = new byte[ Communicate_Code_Length ];

		/// <summary>
		/// 单片机复位返回信息
		/// </summary>
		public const string Information_McuReset = "测试使用的单片机出现了复位的情况，请注意此状态";
		/// <summary>
		/// 单片机超时返回信息
		/// </summary>
		public const string Information_McuTimeOver = "测试使用的单片机出现了响应超时状态，请注意";
		/// <summary>
		/// 单片机发生未知异常返回信息
		/// </summary>
		public const string Information_McuUnknownError = "测试使用的单片机发生未知异常状态，请注意";
		/// <summary>
		/// 单片机无法打开通讯串口时返回信息
		/// </summary>
		public const string Information_McuCannotCommunicationError = "测试使用的单片机出现了不能通讯的情况，请注意此状态";

		#endregion

		#region -- 函数 

		#region -- 私有函数 

		/// <summary>
		/// 返回校验位
		/// </summary>
		/// <param name="data">同步头直到通讯使用的7位字节数据，一共7个元素</param>
		/// <returns>校验位</returns>
		private byte McuControl_vGetCalibrationCode( byte[] data )
		{
			byte code = 0;
			UInt16 added_code = 0;
			for ( byte index = 0 ; index < data.Length ; index++ ) {
				added_code += data[ index ];
			}
			byte[] aByte = BitConverter.GetBytes( added_code );
			code = aByte[ 0 ];
			return code;
		}

		/// <summary>
		/// 使用串口发送指令代码
		/// </summary>
		/// <param name="command_bytes">通讯协议中规定的10字节数组数据</param>
		/// <param name="sp_mcu">单片机使用的串口</param>
		/// <returns >串口发送过程中是否出现异常，string.Empty表示正常</returns>
		private string McuControl_vCommandSend( byte[] command_bytes , ref SerialPort sp_mcu )
		{
			string strInformation = string.Empty;

			/*判断串口打开是否正常，若不正常则先要进行打开设置*/
			if ( !sp_mcu.IsOpen ) {
				Thread.Sleep( 5 );
				try { sp_mcu.Open( ); } catch {
					Thread.Sleep( 5 );
					try { if ( !sp_mcu.IsOpen ) { sp_mcu.Open( ); } } catch {
						strInformation = Information_McuCannotCommunicationError;
						return strInformation;
					}
				}
			}
			/*以下执行串口数据传输指令，先将可能存在的异常数据读取出来，从而保证清除干扰*/
			string temp = sp_mcu.ReadExisting( );
			Thread.Sleep( 1 );
			sp_mcu.Write( command_bytes , 0 , command_bytes.Length );
			return strInformation;
		}

		#endregion

		#region -- 公共函数 

		/// <summary>
		/// 为单片机发送金手指命令，将通讯协议更换为管理员模式
		/// </summary>
		/// <param name="species_name">电源产品的类型的枚举</param>
		/// <param name="sp_mcu">信号测试单片机使用到的串口</param>
		/// <returns>可能存在的异常信息</returns>
		public string McuControl_vInitialize( Product.SpeciesName species_name , ref SerialPort sp_mcu )
		{
			string strInformation = string.Empty;
			byte[] golden_finger;
			switch ( species_name ) {
				case Product.SpeciesName.IG_B1031F__IG_B03S01:

					break;
				case Product.SpeciesName.IG_X1032F:/*3A箱式电源*/
					golden_finger = new byte[] { 0xA5 , 0x77 , 0x30 , 0x01 , 0x4D };
					strInformation = McuControl_vCommandSend( golden_finger , ref sp_mcu );
					break;
				case Product.SpeciesName.IG_X1041F: /*海湾箱式5A*/
				case Product.SpeciesName.IG_X1061F: /*海湾箱式6A*/
				case Product.SpeciesName.IG_X1101F: /*海湾箱式10A*/
				case Product.SpeciesName.IG_X1101H: /*海湾箱式10A*/
				case Product.SpeciesName.IG_X1201F: /*海湾箱式20A*/
				case Product.SpeciesName.IG_X1301F: /*海湾箱式30A*/
				case Product.SpeciesName.IG_B1061F__IG_B06S01:  /*海湾壁挂式6A*/
				case Product.SpeciesName.IG_B1032F:
				case Product.SpeciesName.IG_B2032F: /*声讯电子 12V3A*/
				case Product.SpeciesName.IG_B2032H:
				case Product.SpeciesName.IG_B2022F: /*24V 2A*/				
				case Product.SpeciesName.IG_M1102H:/*泛海三江1U电源改10A*/
				case Product.SpeciesName.IG_M1202H:/*泛海三江1U电源改20A*/
				case Product.SpeciesName.GST_LD_D02H:
				case Product.SpeciesName.IG_M2121F:
				case Product.SpeciesName.IG_B2108:
				case Product.SpeciesName.IG_M3201F__20A主机电源:
				case Product.SpeciesName.GST_LD_D06H:
				case Product.SpeciesName.IG_M2202F:/*泰和安20A*/
				case Product.SpeciesName.IG_M3302F:/*泰和安30A*/
					golden_finger = new byte[] { 0xA5 , 0x30 , 0x01 , 0xD6 };
					strInformation = McuControl_vCommandSend( golden_finger , ref sp_mcu );
					break;
				case Product.SpeciesName.IG_M3202F: //泰和安通讯版本
					golden_finger = new byte[] { 0xE6, 0x03, 0xDF, 0x5F,0xD1 };
					strInformation = McuControl_vCommandSend( golden_finger, ref sp_mcu );
					break;
				case Product.SpeciesName.IG_X1101K://泰和安 需求10A箱式
				case Product.SpeciesName.IG_M2102F: //1U 2A 8A 
				case Product.SpeciesName.IG_M2132F:
				case Product.SpeciesName.IG_M3242F: //1U 8A 8A 8A
				case Product.SpeciesName.IG_M1102F: //1U 10A
				case Product.SpeciesName.IG_M1202F: //1U 20A
				case Product.SpeciesName.IG_M1302F: //1U 30A
				case Product.SpeciesName.IG_B2053F://兼容2055电源
				case Product.SpeciesName.IG_B2053H:
				case Product.SpeciesName.IG_B2053K:
				case Product.SpeciesName.IG_B2073F:
				case Product.SpeciesName.IG_B1051H:
				case Product.SpeciesName.IG_M1101H://尼特所需1U 10A电源
					golden_finger = new byte[] { 0x68 , 0x00 , 0xAB , 0x54 , 0x01 , 0x00 , 0x00 , 0x16 }; //通用协议的校准方式
					strInformation = McuControl_vCommandSend( golden_finger , ref sp_mcu );
					break;
				case Product.SpeciesName.IG_M2131H:
					golden_finger = new byte[] { 0x02 , 0x30 , 0x01 , 0x33 };
					strInformation = McuControl_vCommandSend( golden_finger , ref sp_mcu );
					break;
				case Product.SpeciesName.IG_M1101F__IG_M10S01:

					break;
				case Product.SpeciesName.IG_B2031F__IG_B03D01:

					break;
				case Product.SpeciesName.IG_B2031G__IG_B03D02:

					break;
				case Product.SpeciesName.IG_B2031H__IG_B03D03:

					break;
				case Product.SpeciesName.J_EI8212:
					/*依爱两路隔离10A电源*/
					golden_finger = new byte[] { 0x68 , 0x09 , 0x09 , 0x68 , 0x00 , 0x00 , 0xFF , 0xFF , 0x00 , 0x10 , 0x30 , 0x01 , 0x01 , 0x40 , 0x16 };
					strInformation = McuControl_vCommandSend( golden_finger , ref sp_mcu );
					break;
				case Product.SpeciesName.IG_B1061H:
					golden_finger = new byte[] { 0x55 , 0x88 , 0x01 , 0x89 , 0xED };
					strInformation = McuControl_vCommandSend( golden_finger , ref sp_mcu );
					break;
				case Product.SpeciesName.IG_Z2071F://应急照明电源 3节电池 300W
				case Product.SpeciesName.IG_Z2102F://应急照明电源 2节电池 300W
				case Product.SpeciesName.IG_Z2121F://应急照明电源 3节电池 500W
				case Product.SpeciesName.IG_Z2182F://应急照明电源 2节电池 500W
				case Product.SpeciesName.IG_Z2181F://应急照明电源 3节电池 750W
				case Product.SpeciesName.IG_Z2272F://应急照明电源 2节电池 750W
				case Product.SpeciesName.IG_Z2102L://赋安专用应急照明电源 2节电池 300W
				case Product.SpeciesName.IG_Z2182L://赋安专用应急照明电源 2节电池 500W
				case Product.SpeciesName.IG_Z2272L://赋安专用应急照明电源 2节电池 750W
				case Product.SpeciesName.IG_Z1203F://海湾应急照明电源 2节电池 800W 升压
				case Product.SpeciesName.IG_Z2244F://其他用户应急照明电源 3节电池 1000W
					golden_finger = new byte[] { 0x68 , 0x00 , 0x01 , 0x68 , 0xA9 , 0xAA , 0x16 };
					strInformation = McuControl_vCommandSend( golden_finger , ref sp_mcu );
					break;
				default:
					break;
			}
			return strInformation;
		}

		/// <summary>
		/// 检测到串口接收数据之后进行的接收参数获取与判断
		/// </summary>
		/// <param name="sp_mcu">目标串口</param>
		/// <returns>返回值的异常状态</returns>
		string McuControl_vComRespond( ref SerialPort sp_mcu )
		{
			string strInformation = string.Empty;
			/*将串口受到的数据移到aByte数组中，并依据读取的数量进行判断*/
			sp_mcu.Read( Serialport_Redata , 0 , Serialport_Redata.Length );
			/*清空接收区缓存,防止对下次接收数据的干扰*/
			string stri = sp_mcu.ReadExisting( );
			//依据校验命令判断上位机给下位机发送的指令代码是否正常
			if ( Serialport_Redata[ 3 ] == Check ) {
				if ( Serialport_Redata[ 4 ] != ( byte ) 0x80 ) {
					strInformation = Information_McuUnknownError;
				}
			} else {	
				strInformation = Information_McuUnknownError;
			}

			/*本次程序的硬件上推荐使用485通讯，而非232(TTL电平)通讯，不要将串口关闭*/
			return strInformation;
		}

        /// <summary>
        /// 向MCU发送指令
        /// </summary>
        /// <param name="command">读、写、复位指令</param>
        /// <param name="config">MCU与PC通讯使用到的Config码</param>
        /// <param name="sp_mcu">对应的串口</param>
        /// <param name="data0">可选的byte型参数0</param>
        /// <param name="data1">可选的byte型参数1</param>
        /// <param name="data2">可选的byte型参数2</param>
        /// <param name="data3">可选的byte型参数3</param>
        /// <returns>指令响应状态说明</returns>
        public string McuControl_vAssign( Command command, Config config, ref SerialPort sp_mcu, byte data0 = 0, byte data1 = 0, byte data2 = 0, byte data3 = 0)
        {
            string strInformation = string.Empty;
            /*通讯指令存储*/
            byte [ ] send_data = new byte [ Communicate_Code_Length ];
            byte [ ] send_command = new byte [ Communicate_Code_Length - 2 ];
            send_data [ 0 ] = Header;
            send_data [ 1 ] = Convert.ToByte( command );
            send_data [ 2 ] = Convert.ToByte( config );
            //填充Data0~Data3到数组中
            send_data [ 3 ] = data0;
            send_data [ 4 ] = data1;
            send_data [ 5 ] = data2;
            send_data [ 6 ] = data3;

            //将指令数组SendData中前7个字节的元素复制到SendCommand数组中
            System.Buffer.BlockCopy( send_data, 0, send_command, 0, send_command.Length );
            send_data [ 7 ] = McuControl_vGetCalibrationCode( send_command );
            send_data [ 8 ] = End;
            //发送命令指令
            strInformation = McuControl_vCommandSend( send_data, ref sp_mcu );
            if (strInformation != string.Empty) { return strInformation; }
            Int32 waittime = 0;
            while (sp_mcu.BytesToRead == 0)
            {
                Thread.Sleep( 5 );
                if (++waittime > 120)
                {
                    //超时处理方式，不处理，待人工判断
                    strInformation = Information_McuTimeOver;
                    return strInformation;
                }
            }
			Thread.Sleep( 300 );
            strInformation = McuControl_vComRespond( ref sp_mcu );

            return strInformation;
        }

		/// <summary>
		/// 设置可以交由用户设置的过功率保护的相关值
		/// </summary>
		/// <param name="target_oppsignal">目标OPP信号值</param>
		/// <param name="species_name">产品种类</param>
		/// <param name="sp_mcu">使用到的串口</param>
		/// <returns></returns>
		public string McuControl_vSetOPPSignal( ushort target_oppsignal , Product.SpeciesName species_name , ref SerialPort sp_mcu )
		{
			string strInformation = string.Empty;
			byte[] value_1;
			switch ( species_name ) {
				case Product.SpeciesName.IG_M1102F://1U 10A
				case Product.SpeciesName.IG_M1202F://1U 20A
				case Product.SpeciesName.IG_M1302F://1U 30A 
					byte[] temp = BitConverter.GetBytes( target_oppsignal );
					value_1 = new byte[10]; //通用协议的设置方式
					value_1[ 0 ] = 0x68;
					value_1[ 1 ] = 0x00;
					value_1[ 2 ] = 0x21;
					value_1[ 3 ] = 0xDE;
					value_1[ 4 ] = 0x03;
					value_1[ 5 ] = 0x00;
					value_1[ 6 ] = temp[ 0 ];
					value_1[ 7 ] = temp[ 1 ];
					value_1[ 8 ] = Convert.ToByte( value_1[ 5 ] + value_1[ 6 ] + value_1[ 7 ] );
					value_1[ 9 ] = 0x16;
					strInformation = McuControl_vCommandSend( value_1 , ref sp_mcu );
					break;			
				default:
					break;
			}
			return strInformation;
		}

		public string McuControl_vSetBeepTime( ushort target_beeptime , Product.SpeciesName species_name , ref SerialPort sp_mcu )
		{
			int index = 0;
			int last_recount = 0;
			string strInformation = string.Empty;
			byte[] value_1;
			sp_mcu.ReadExisting();
			if ((species_name >= Product.SpeciesName.IG_Z2071F) && (species_name <= Product.SpeciesName.IG_Z2272L)) {
				//应急照明电源的蜂鸣器工作时间			
				value_1 = new byte[ 9 ]; //通用协议的设置方式
				value_1[ 0 ] = 0x68;
				value_1[ 1 ] = 0x00;
				value_1[ 2 ] = 0x03;
				value_1[ 3 ] = 0x68;
				value_1[ 4 ] = 0x2A;
				value_1[ 5 ] = Convert.ToByte( ((target_beeptime / 1000) << 4) | ((target_beeptime % 1000) / 100) );
				value_1[ 6 ] = Convert.ToByte( (((target_beeptime % 100) / 10) << 4) | (target_beeptime % 10) );
				value_1[ 7 ] = Convert.ToByte( value_1[ 2 ] + value_1[ 4 ] + value_1[ 5 ] + value_1[ 6 ] );
				value_1[ 8 ] = 0x16;
				strInformation = McuControl_vCommandSend( value_1, ref sp_mcu );
				//等待回码之后将接收到的缓存数据清除
				do {
					if ((sp_mcu.BytesToRead == last_recount) && (last_recount != 0) && (index > 20)) { break; }
					Thread.Sleep( 15 );
					last_recount = sp_mcu.BytesToRead;
				} while (++index < 100);
				if (sp_mcu.BytesToRead == 0) { return "蜂鸣器工作时间设置异常"; }
				byte[] data = new byte[ sp_mcu.BytesToRead ];
				sp_mcu.Read( data, 0, sp_mcu.BytesToRead );
				if (!((data[ 0 ] == 0x68) && (data[ 4 ] == 0x10))) {
					return "蜂鸣器工作时间设置异常";
				}
			 	
			} else if ((species_name == Product.SpeciesName.IG_M1202F) || (species_name == Product.SpeciesName.IG_M1102F) || (species_name == Product.SpeciesName.IG_M1302F)) { 
				value_1 = new byte[ 9 ]; //通用协议的设置方式
				value_1[ 0 ] = 0x68;
				value_1[ 1 ] = 0x00;
				value_1[ 2 ] = 0x2C;
				value_1[ 3 ] = 0xD3;
				value_1[ 4 ] = 0x02;
				Byte[] temp = BitConverter.GetBytes( target_beeptime );
				value_1[ 5 ] = temp[ 0 ];
				if (temp.Length > 1) {
					value_1[ 6 ] = temp[ 1 ];
				} else {
					value_1[ 6 ] = 0;
				}
				value_1[ 7 ] = Convert.ToByte( value_1[ 5 ] + value_1[ 6 ] );
				value_1[ 8 ] = 0x16;
				strInformation = McuControl_vCommandSend( value_1, ref sp_mcu );
			}
			return strInformation;
		}


		public string McuControl_vSetBatCutoffVoltage( ushort cutoff_voltage , Product.SpeciesName species_name , ref SerialPort sp_mcu )
		{
			int index = 0;
			int last_recount = 0;
			string strInformation = string.Empty;
			byte[] value_1;
			if ( ( species_name >= Product.SpeciesName.IG_Z2071F ) && ( species_name <= Product.SpeciesName.IG_Z2272L ) ) {
				//应急照明电源的蜂鸣器工作时间			
				value_1 = new byte[ 9 ]; //通用协议的设置方式
				value_1[ 0 ] = 0x68;
				value_1[ 1 ] = 0x00;
				value_1[ 2 ] = 0x03;
				value_1[ 3 ] = 0x68;
				value_1[ 4 ] = 0x20;
				value_1[ 5 ] = Convert.ToByte( ( ( cutoff_voltage / 1000 ) << 4 ) | ( ( cutoff_voltage % 1000 ) / 100 ) );
				value_1[ 6 ] = Convert.ToByte( ( ( ( cutoff_voltage % 100 ) / 10 ) << 4 ) | ( cutoff_voltage % 10 ) );
				value_1[ 7 ] = Convert.ToByte( value_1[ 2 ]  + value_1[ 4 ] + value_1[ 5 ] + value_1[ 6 ] );
				value_1[ 8 ] = 0x16;
				strInformation = McuControl_vCommandSend( value_1 , ref sp_mcu );
				//等待回码之后将接收到的缓存数据清除
				do {
					if ((sp_mcu.BytesToRead == last_recount) && (last_recount != 0) && (index > 20)) { break; }
					Thread.Sleep( 15 );
					last_recount = sp_mcu.BytesToRead;
				} while (++index < 100);
				if (sp_mcu.BytesToRead == 0) { return "备电切断点设置异常"; }
				byte[] data = new byte[ sp_mcu.BytesToRead ];
				sp_mcu.Read( data, 0, sp_mcu.BytesToRead );
				if (!((data[ 0 ] == 0x68) && (data[ 4 ] == 0x10))) {
					return "备电切断点设置异常";
				}
			}
			return strInformation;
		}

		public string McuControl_vSetBatUnderVoltage( ushort under_voltage , Product.SpeciesName species_name , ref SerialPort sp_mcu )
		{
			int index = 0;
			int last_recount = 0;
			string strInformation = string.Empty;
			byte[] value_1;
			if ( ( species_name >= Product.SpeciesName.IG_Z2071F ) && ( species_name <= Product.SpeciesName.IG_Z2272L ) ) {
				//应急照明电源的蜂鸣器工作时间			
				value_1 = new byte[ 9 ]; //通用协议的设置方式
				value_1[ 0 ] = 0x68;
				value_1[ 1 ] = 0x00;
				value_1[ 2 ] = 0x03;
				value_1[ 3 ] = 0x68;
				value_1[ 4 ] = 0x28;
				value_1[ 5 ] = Convert.ToByte( ( ( under_voltage / 1000 ) << 4 ) | ( ( under_voltage % 1000 ) / 100 ) );
				value_1[ 6 ] = Convert.ToByte( ( ( ( under_voltage % 100 ) / 10 ) << 4 ) | ( under_voltage % 10 ) );
				value_1[ 7 ] = Convert.ToByte( value_1[ 2 ] + value_1[ 4 ] + value_1[ 5 ] + value_1[ 6 ] );
				value_1[ 8 ] = 0x16;
				strInformation = McuControl_vCommandSend( value_1 , ref sp_mcu );
				//等待回码之后将接收到的缓存数据清除
				do {
					if ((sp_mcu.BytesToRead == last_recount) && (last_recount != 0) && (index > 20)) { break; }
					Thread.Sleep( 15 );
					last_recount = sp_mcu.BytesToRead;
				} while (++index < 100);
				if (sp_mcu.BytesToRead == 0) { return "备电欠压点设置异常"; }
				byte[] data = new byte[ sp_mcu.BytesToRead ];
				sp_mcu.Read( data, 0, sp_mcu.BytesToRead );
				if (!((data[ 0 ] == 0x68) && (data[ 4 ] == 0x10))) {
					return "备电欠压点设置异常";
				}
			}
			return strInformation;
		}

		/// <summary>
		/// 对电源设置过功率点
		/// </summary>
		/// <param name="product_Model">产品型号</param>
		/// <param name="op_signal_value">过功率信号对应值</param>
		/// <param name="sp_mcu">使用的串口</param>
		/// <returns>可能存在的异常</returns>
		public string McuControl_vSetOverpowrSignal(Product.SpeciesName product_Model, decimal op_signal_value, ref SerialPort sp_mcu)
		{
			string strInformation = string.Empty;
			byte[] value_1;
			int index = 0;
			int last_recount = 0;
			int temp = Convert.ToInt32( op_signal_value );
			switch (product_Model) {
				case Product.SpeciesName.IG_Z2071F:
				case Product.SpeciesName.IG_Z2102F:
				case Product.SpeciesName.IG_Z2102L:
				case Product.SpeciesName.IG_Z2121F:
				case Product.SpeciesName.IG_Z2181F:
				case Product.SpeciesName.IG_Z2182F:
				case Product.SpeciesName.IG_Z2182L:
				case Product.SpeciesName.IG_Z2272F:
				case Product.SpeciesName.IG_Z2272L:
					//应急照明电源的蜂鸣器工作时间			
					value_1 = new byte[ 9 ]; //通用协议的设置方式
					value_1[ 0 ] = 0x68;
					value_1[ 1 ] = 0x00;
					value_1[ 2 ] = 0x03;
					value_1[ 3 ] = 0x68;
					value_1[ 4 ] = 0x22;
					value_1[ 5 ] = Convert.ToByte( ((temp / 1000) << 4) | ((temp % 1000) / 100) );
					value_1[ 6 ] = Convert.ToByte( (((temp % 100) / 10) << 4) | (temp % 10) );
					value_1[ 7 ] = Convert.ToByte( value_1[ 2 ] + value_1[ 4 ] + value_1[ 5 ] + value_1[ 6 ] );
					value_1[ 8 ] = 0x16;
					strInformation = McuControl_vCommandSend( value_1, ref sp_mcu );
					//等待回码之后将接收到的缓存数据清除
					do {
						if ((sp_mcu.BytesToRead == last_recount) && (last_recount != 0) && (index > 20)) { break; }
						Thread.Sleep( 15 );
						last_recount = sp_mcu.BytesToRead;
					} while (++index < 100);
					if (sp_mcu.BytesToRead == 0) { return "过功率点设置异常"; }
					byte[] data = new byte[ sp_mcu.BytesToRead ];
					sp_mcu.Read( data, 0, sp_mcu.BytesToRead );
					if (!((data[ 0 ] == 0x68) && (data[ 4 ] == 0x10))) {
						return "过功率点设置异常";
					}
					break;
				default:
					break;
			}
			return strInformation;
		}

		#endregion

		#endregion

		#endregion

		#region -- 垃圾回收机制 

		private bool disposed = false;   // 保证多次调用Dispose方式不会抛出异常

        #region IDisposable 成员

        /// <summary>
        /// 释放内存中所占的资源
        /// </summary>
        public void Dispose()
        {
            Dispose( true );
            GC.SuppressFinalize( this );
        }

        #endregion

        /// <summary>
        /// 无法直接调用的资源释放程序
        /// </summary>
        /// <param name="disposing"></param>
        protected virtual void Dispose(bool disposing)
        {
            if (disposed) { return; }
            if (disposing)      // 在这里释放托管资源
            {

            }
            // 在这里释放非托管资源


            disposed = true; // Indicate that the instance has been disposed                      
        }

        /// <summary>
        /// 类析构函数
        /// </summary>
        ~MCU_Control()
        {
            // 为了保持代码的可读性性和可维护性,千万不要在这里写释放非托管资源的代码 
            // 必须以Dispose(false)方式调用,以false告诉Dispose(bool disposing)函数是从垃圾回收器在调用Finalize时调用的 
            Dispose( false );    // MUST be false
        }

        #endregion
    }
}
