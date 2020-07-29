using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.IO.Ports;
using System.Threading;
using Instrument_Control;


namespace ACPowerControl
{
	/// <summary>
	/// MainWindow.xaml 的交互逻辑
	/// </summary>
	public partial class MainWindow : Window
	{
		public MainWindow()
		{
			InitializeComponent( );
			/*查看串口是否可用 - 至少1个有效串口存在*/
			if ( SerialPort.GetPortNames( ).LongLength <= 0 ) {
				/*提示错误*/
				MessageBox.Show( "系统缺少可用的串口，请确保存在正确的串口后再执行本程序" , "异常报警" , MessageBoxButton.OK , MessageBoxImage.Error );
				this.Close( );
			}
			/*绑定事件*/
			chkOutput1.Checked += ChkOutput_Checked;
			chkOutput2.Checked += ChkOutput_Checked;
			chkOutput3.Checked += ChkOutput_Checked;

			chkOutput1.Unchecked += ChkOutput_Unchecked;
			chkOutput2.Unchecked += ChkOutput_Unchecked;
			chkOutput3.Unchecked += ChkOutput_Unchecked;

		}

		bool switch_value = false; //标记switch button控件的值
		bool power_value = false; //标记power button控件的值

		const string Calibration_Error = "校准过程中出现了故障";
		const string AbrotCalibration = "用户终止校准";
		const Int32 CR_VALUE = 50000;   //使用CR模式放电的带载电阻值

		/*声明需要使用于交流程控电源的串口对象和通用串口，可以先不实例化*/
		SerialPort sp_acpower;
		SerialPort sp_common;
		SerialPort sp_product;
		/*操作程控电源使用的线程的声明*/
		Thread trdACPowerWorking;
		Thread trdMainpowerChanging;

		Thread trdCalibration;
		bool Main_bBeepWorkingTimeKeepDefault = false;    //蜂鸣器在备电关断之后所工作的时间的标记
		bool CanUseUartAfterCalibration = false;//默认校准后需要禁用串口功能（特殊型号的电源使用）
		bool ShouldCalibrateSpCurrent = true;//默认应急照明电源在校准时需要进行备电工作状态下的电流校准操作
	
		/*默认的3个输出使用的电子负载的实际使用情况 及 用户自定义负载电流*/
		bool[] UseLoad = new bool[] { false , false , false };
		decimal[] UserSettedCurrent = new decimal[] { 0 , 0 , 0 };
		int[] LoadChannel = new int[] { 0 , 0 , 0 };        //负载对应输出通道的分配情况

		CancellationTokenSource ctsExitVoltageStepChange;   //主电电压渐变取消事件
		CancellationTokenSource ctsExitMainpowerSwitch;     //主电通9断1实验取消事件

		private delegate void dlgMain_vEnableSet( Control control , bool enable );
		private void Main_vEnableSet( Control control , bool enable )
		{
			control.IsEnabled = enable;
		}

		private void BtnChange_Click( object sender , RoutedEventArgs e )
		{
			string error_information = string.Empty;
			ushort voltage, frequency;
			try {
				decimal voltage_target = Convert.ToDecimal( txtVoltage.Text );
				decimal frequency_target = Convert.ToDecimal( txtFrequency.Text );

				voltage_target *= 10m;
				frequency_target *= 10m;

				voltage = Convert.ToUInt16( voltage_target );
				frequency = Convert.ToUInt16( frequency_target );

			} catch {
				MessageBox.Show( "请检查设定的目标值和选定使用的串口是否正常" , "故障报警" ); return;
			}

			using ( AN97002H acpower = new AN97002H( ) ) {
				error_information = acpower.ACPower_vSetParameters( 12 , voltage , frequency , 5 , 5 , 1 , false , ref sp_acpower );
				if ( error_information != string.Empty ) {
					MessageBox.Show( "设置状态错误，请重试" );
				}
			}
		}

		private void Cob_PreviewMouseDown( object sender , MouseButtonEventArgs e )
		{
			ComboBox cob = sender as ComboBox;
			/*获取当前电脑上的串口集合，并将其显示在cob控件中*/
			string[] SerialPortlist = SerialPort.GetPortNames( );
			cob.Items.Clear( );
			foreach ( string name in SerialPortlist ) {
				cob.Items.Add( name );
			}
		}

		private void ArrowButton1_Click( object sender , RoutedEventArgs e )
		{
			string error_information = string.Empty;
			try {
				using ( AN97002H acpower = new AN97002H( ) ) {
					error_information = acpower.ACPower_vQueryWorkingStatus( 12 , ref sp_acpower );
					if ( error_information != string.Empty ) { MessageBox.Show( "获取值错误" ); return; }
					AN97002H.Working_Status working_status = new AN97002H.Working_Status( );
					working_status = acpower.ACPower_vGetWorkingStatus( );
					decimal voltage = 0.0m, frequency = 0.0m;
					if ( working_status == AN97002H.Working_Status.Running_Status ) {
						error_information = acpower.ACPower_vQueryResult( 12 , ref sp_acpower );
						if ( error_information != string.Empty ) { MessageBox.Show( "获取值错误" ); return; }
						AN97002H.Parameters parameters = new AN97002H.Parameters( );
						parameters = acpower.ACPower_vGetParameters( );
						voltage = parameters.Voltage;
						frequency = parameters.Frequency;
					} else if ( working_status == AN97002H.Working_Status.Await_Status ) {
						error_information = acpower.ACPower_vQuerySettingParameters( 12 , ref sp_acpower );
						if ( error_information != string.Empty ) { MessageBox.Show( "获取值错误" ); return; }
						AN97002H.Setting_Parameters parameters = new AN97002H.Setting_Parameters( );
						parameters = acpower.ACPower_vGetSettingParameters( );
						voltage = parameters.Voltage;
						frequency = parameters.Frequency;
					}

					voltage += 1.0m;

					voltage *= 10m;
					frequency *= 10m;

					error_information = acpower.ACPower_vSetParameters( 12 , Convert.ToUInt16( voltage ) , Convert.ToUInt16( frequency ) , 5 , 5 , 1 , false , ref sp_acpower );
					if ( error_information != string.Empty ) {
						MessageBox.Show( "设置状态错误，请重试" ); return;
					}

					voltage /= 10m;
					txtVoltage.Text = voltage.ToString( );
				}
			} catch {
				MessageBox.Show( "请检查程控电源选择的串口是否正常" , "故障报警" );
			}
		}

		private void ArrowButton2_Click( object sender , RoutedEventArgs e )
		{
			string error_information = string.Empty;
			try {
				using ( AN97002H acpower = new AN97002H( ) ) {
					error_information = acpower.ACPower_vQueryWorkingStatus( 12 , ref sp_acpower );
					if ( error_information != string.Empty ) { MessageBox.Show( "获取值错误" ); return; }
					AN97002H.Working_Status working_status = new AN97002H.Working_Status( );
					working_status = acpower.ACPower_vGetWorkingStatus( );
					decimal voltage = 0.0m, frequency = 0.0m;
					if ( working_status == AN97002H.Working_Status.Running_Status ) {
						error_information = acpower.ACPower_vQueryResult( 12 , ref sp_acpower );
						if ( error_information != string.Empty ) { MessageBox.Show( "获取值错误" ); return; }
						AN97002H.Parameters parameters = new AN97002H.Parameters( );
						parameters = acpower.ACPower_vGetParameters( );
						voltage = parameters.Voltage;
						frequency = parameters.Frequency;
					} else if ( working_status == AN97002H.Working_Status.Await_Status ) {
						error_information = acpower.ACPower_vQuerySettingParameters( 12 , ref sp_acpower );
						if ( error_information != string.Empty ) { MessageBox.Show( "获取值错误" ); return; }
						AN97002H.Setting_Parameters parameters = new AN97002H.Setting_Parameters( );
						parameters = acpower.ACPower_vGetSettingParameters( );
						voltage = parameters.Voltage;
						frequency = parameters.Frequency;
					}
					voltage -= 1.0m;

					voltage *= 10m;
					frequency *= 10m;

					error_information = acpower.ACPower_vSetParameters( 12 , Convert.ToUInt16( voltage ) , Convert.ToUInt16( frequency ) , 5 , 5 , 1 , false , ref sp_acpower );
					if ( error_information != string.Empty ) {
						MessageBox.Show( "设置状态错误，请重试" ); return;
					}
					voltage /= 10m;
					txtVoltage.Text = voltage.ToString( );
				}
			} catch {
				MessageBox.Show( "请检查程控电源使用的串口是否正常" , "故障报警" );
			}
		}

		private void ArrowButton3_Click( object sender , RoutedEventArgs e )
		{
			string error_information = string.Empty;
			try {
				using ( AN97002H acpower = new AN97002H( ) ) {
					error_information = acpower.ACPower_vQueryWorkingStatus( 12 , ref sp_acpower );
					if ( error_information != string.Empty ) { MessageBox.Show( "获取值错误" ); return; }
					AN97002H.Working_Status working_status = new AN97002H.Working_Status( );
					working_status = acpower.ACPower_vGetWorkingStatus( );
					decimal voltage = 0.0m, frequency = 0.0m;
					if ( working_status == AN97002H.Working_Status.Running_Status ) {
						error_information = acpower.ACPower_vQueryResult( 12 , ref sp_acpower );
						if ( error_information != string.Empty ) { MessageBox.Show( "获取值错误" ); return; }
						AN97002H.Parameters parameters = new AN97002H.Parameters( );
						parameters = acpower.ACPower_vGetParameters( );
						voltage = parameters.Voltage;
						frequency = parameters.Frequency;
					} else if ( working_status == AN97002H.Working_Status.Await_Status ) {
						error_information = acpower.ACPower_vQuerySettingParameters( 12 , ref sp_acpower );
						if ( error_information != string.Empty ) { MessageBox.Show( "获取值错误" ); return; }
						AN97002H.Setting_Parameters parameters = new AN97002H.Setting_Parameters( );
						parameters = acpower.ACPower_vGetSettingParameters( );
						voltage = parameters.Voltage;
						frequency = parameters.Frequency;
					}
					frequency += 1.0m;

					voltage *= 10m;
					frequency *= 10m;

					error_information = acpower.ACPower_vSetParameters( 12 , Convert.ToUInt16( voltage ) , Convert.ToUInt16( frequency ) , 5 , 5 , 1 , false , ref sp_acpower );
					if ( error_information != string.Empty ) {
						MessageBox.Show( "设置状态错误，请重试" ); return;
					}
					frequency /= 10m;
					txtFrequency.Text = frequency.ToString( );
				}
			} catch {
				MessageBox.Show( "请检查程控电源使用的串口是否正常" , "故障报警" );
			}
		}

		private void ArrowButton4_Click( object sender , RoutedEventArgs e )
		{
			string error_information = string.Empty;
			try {
				using ( AN97002H acpower = new AN97002H( ) ) {
					error_information = acpower.ACPower_vQueryWorkingStatus( 12 , ref sp_acpower );
					if ( error_information != string.Empty ) { MessageBox.Show( "获取值错误" ); return; }
					AN97002H.Working_Status working_status = new AN97002H.Working_Status( );
					working_status = acpower.ACPower_vGetWorkingStatus( );
					decimal voltage = 0.0m, frequency = 0.0m;
					if ( working_status == AN97002H.Working_Status.Running_Status ) {
						error_information = acpower.ACPower_vQueryResult( 12 , ref sp_acpower );
						if ( error_information != string.Empty ) { MessageBox.Show( "获取值错误" ); return; }
						AN97002H.Parameters parameters = new AN97002H.Parameters( );
						parameters = acpower.ACPower_vGetParameters( );
						voltage = parameters.Voltage;
						frequency = parameters.Frequency;
					} else if ( working_status == AN97002H.Working_Status.Await_Status ) {
						error_information = acpower.ACPower_vQuerySettingParameters( 12 , ref sp_acpower );
						if ( error_information != string.Empty ) { MessageBox.Show( "获取值错误" ); return; }
						AN97002H.Setting_Parameters parameters = new AN97002H.Setting_Parameters( );
						parameters = acpower.ACPower_vGetSettingParameters( );
						voltage = parameters.Voltage;
						frequency = parameters.Frequency;
					}
					frequency -= 1.0m;

					voltage *= 10m;
					frequency *= 10m;

					error_information = acpower.ACPower_vSetParameters( 12 , Convert.ToUInt16( voltage ) , Convert.ToUInt16( frequency ) , 5 , 5 , 1 , false , ref sp_acpower );
					if ( error_information != string.Empty ) {
						MessageBox.Show( "设置状态错误，请重试" ); return;
					}
					frequency /= 10m;
					txtFrequency.Text = frequency.ToString( );
				}
			} catch {
				MessageBox.Show( "请检查程控交流电源使用的串口是否正常" , "故障报警" );
			}
		}

		/// <summary>
		/// 执行电压的步进操作；输出电压连续变化
		/// </summary>
		/// <param name="sender"></param>
		/// <param name="e"></param>
		private void BtnStartStepWork_Click( object sender , RoutedEventArgs e )
		{
			decimal started_voltage = 0, ended_voltage = 0, step_voltage = 0, step_period = 0;
			decimal temp = 0;
			try {
				started_voltage = Convert.ToDecimal( txtStartVoltage.Text ); started_voltage *= 10;
				ended_voltage = Convert.ToDecimal( txtEndVoltage.Text ); ended_voltage *= 10;
				step_voltage = Convert.ToDecimal( txtStepVoltage.Text ); step_voltage *= 10;
				step_period = Convert.ToDecimal( txtStepPeriod.Text );
			} catch {
				MessageBox.Show( "所需要的关键参数输入不全，请补全信息" , "异常提示" );
				return;
			}

			/*判断按键对象*/
			Button btn = new Button( );
			btn = sender as Button;
			if ( btn == btnRevrse ) {
				temp = started_voltage;
				started_voltage = ended_voltage;
				ended_voltage = temp;
			}

			/*需要在新线程中进行对程控电源的控制*/
			if ( trdACPowerWorking != null ) {
				if ( trdACPowerWorking.ThreadState != ThreadState.Stopped ) { return; }
			}

			if ( trdACPowerWorking == null ) {
				trdACPowerWorking = new Thread( () => Main_vStepVoltageWorking( Convert.ToUInt16( started_voltage ) , Convert.ToUInt16( ended_voltage ) , Convert.ToUInt16( step_voltage ) , Convert.ToUInt16( step_period ) ) ) {
					Name = "电压的步进操作线程" ,
					Priority = ThreadPriority.AboveNormal ,
					IsBackground = true
				};
				trdACPowerWorking.SetApartmentState( ApartmentState.STA );
			} else {
				trdACPowerWorking = new Thread( () => Main_vStepVoltageWorking( Convert.ToUInt16( started_voltage ) , Convert.ToUInt16( ended_voltage ) , Convert.ToUInt16( step_voltage ) , Convert.ToUInt16( step_period ) ) );
			}

			switch_value = false;
			ctsExitVoltageStepChange = new CancellationTokenSource( );

			trdACPowerWorking.Start( );
		}

		/// <summary>
		/// 步进的线程操作
		/// </summary>
		/// <param name="started_voltage"></param>
		/// <param name="ended_voltage"></param>
		/// <param name="step_voltage"></param>
		/// <param name="step_period"></param>
		/// <returns></returns>
		private void Main_vStepVoltageWorking( ushort started_voltage , ushort ended_voltage , ushort step_voltage , ushort step_period )
		{
			string error_information = string.Empty;
			try {
				using ( AN97002H acpower = new AN97002H( ) ) {
					/*获取当前电源设置的频率参数*/
					error_information = acpower.ACPower_vQueryResult( 12 , ref sp_acpower );
					if ( error_information != string.Empty ) { MessageBox.Show( "设置状态错误，请重试" ); return; }
					AN97002H.Parameters parameters = new AN97002H.Parameters( );
					parameters = acpower.ACPower_vGetParameters( );
					ushort frequency = Convert.ToUInt16( parameters.Frequency * 10 );
					if ( started_voltage > ended_voltage ) {
						for ( ushort voltage = started_voltage ; voltage >= ended_voltage ; voltage -= step_voltage ) {
							error_information = acpower.ACPower_vSetParameters( 12 , voltage , frequency , 5 , 5 , 1 , false , ref sp_acpower );
							if ( error_information != string.Empty ) {
								//                           MessageBox.Show("设置状态错误，请重试");
							} else {
								/*将当前的电压数据委托显示在前面板上*/
								Dispatcher.Invoke( new dlgMain_vDisplayValue( Main_vDisplayValue ) , txtVoltage , voltage );
							}
							Thread.Sleep( 1000 * step_period );
							if ( ctsExitVoltageStepChange.IsCancellationRequested ) { break; }

						}
					} else {
						for ( ushort voltage = started_voltage ; voltage <= ended_voltage ; voltage += step_voltage ) {
							error_information = acpower.ACPower_vSetParameters( 12 , voltage , frequency , 5 , 5 , 1 , false , ref sp_acpower );
							if ( error_information != string.Empty ) {
								//                           MessageBox.Show("设置状态错误，请重试");
							} else {
								/*将当前的电压数据委托显示在前面板上*/
								Dispatcher.Invoke( new dlgMain_vDisplayValue( Main_vDisplayValue ) , txtVoltage , voltage );
							}
							Thread.Sleep( 1000 * step_period );
							if ( ctsExitVoltageStepChange.IsCancellationRequested ) { break; }
						}
					}
				}
			} catch {
				MessageBox.Show( "请检查程控交流电源使用的串口是否正常" , "故障报警" );
			}
		}

		private delegate void dlgMain_vDisplayValue( TextBox control , ushort value );
		private void Main_vDisplayValue( TextBox control , ushort value )
		{
			decimal new_value = Convert.ToDecimal( value );
			new_value /= 10;
			control.Text = new_value.ToString( );
		}

		private void BtnStepFrequencyWork_Click( object sender , RoutedEventArgs e )
		{
			decimal started_frequency = 0, ended_frequency = 0, step_frequency = 0, step_frequency_period = 0;
			decimal temp = 0;
			try {
				started_frequency = Convert.ToDecimal( txtStartedFrequency.Text ); started_frequency *= 10;
				ended_frequency = Convert.ToDecimal( txtEndedFrequency.Text ); ended_frequency *= 10;
				step_frequency = Convert.ToDecimal( txtStepFrequency.Text ); step_frequency *= 10;
				step_frequency_period = Convert.ToDecimal( txtStepFrequencyPeriod.Text );
			} catch {
				MessageBox.Show( "所需要的关键参数输入不全，请补全信息" , "异常提示" );
				return;
			}

			/*判断按键对象*/
			Button btn = new Button( );
			btn = sender as Button;
			if ( btn == btnRevrseFrequency ) {
				temp = started_frequency;
				started_frequency = ended_frequency;
				ended_frequency = temp;
			}

			/*需要在新线程中进行对程控电源的控制*/
			if ( trdACPowerWorking != null ) {
				if ( trdACPowerWorking.ThreadState != ThreadState.Stopped ) { return; }
			}

			if ( trdACPowerWorking == null ) {
				trdACPowerWorking = new Thread( () => Main_vStepFrequcyWorking( Convert.ToUInt16( started_frequency ) , Convert.ToUInt16( ended_frequency ) , Convert.ToUInt16( step_frequency ) , Convert.ToUInt16( step_frequency_period ) ) ) {
					Name = "频率的步进操作线程" ,
					Priority = ThreadPriority.AboveNormal ,
					IsBackground = true
				};
				trdACPowerWorking.SetApartmentState( ApartmentState.STA );
			} else {
				trdACPowerWorking = new Thread( () => Main_vStepFrequcyWorking( Convert.ToUInt16( started_frequency ) , Convert.ToUInt16( ended_frequency ) , Convert.ToUInt16( step_frequency ) , Convert.ToUInt16( step_frequency_period ) ) );
			}
			trdACPowerWorking.Start( );
		}

		private void Main_vStepFrequcyWorking( ushort started_frequency , ushort ended_frequency , ushort step_frequency , ushort step_frequency_period )
		{
			string error_information = string.Empty;
			try {
				using ( AN97002H acpower = new AN97002H( ) ) {
					/*获取当前电源设置的频率参数*/
					error_information = acpower.ACPower_vQueryResult( 12 , ref sp_acpower );
					if ( error_information != string.Empty ) { MessageBox.Show( "设置状态错误，请重试" ); return; }
					AN97002H.Parameters parameters = new AN97002H.Parameters( );
					parameters = acpower.ACPower_vGetParameters( );
					ushort voltage = Convert.ToUInt16( parameters.Voltage * 10 );
					if ( started_frequency > ended_frequency ) {
						for ( ushort frequency = started_frequency ; frequency >= ended_frequency ; frequency -= step_frequency ) {
							error_information = acpower.ACPower_vSetParameters( 12 , voltage , frequency , 5 , 5 , 1 , false , ref sp_acpower );
							if ( error_information != string.Empty ) {
								//                           MessageBox.Show("设置状态错误，请重试");
							} else {
								/*将当前的电压数据委托显示在前面板上*/
								Dispatcher.Invoke( new dlgMain_vDisplayValue( Main_vDisplayValue ) , txtFrequency , frequency );
							}
							Thread.Sleep( 1000 * step_frequency_period );
						}
					} else {
						for ( ushort frequency = started_frequency ; frequency <= ended_frequency ; frequency += step_frequency ) {
							error_information = acpower.ACPower_vSetParameters( 12 , voltage , frequency , 5 , 5 , 1 , false , ref sp_acpower );
							if ( error_information != string.Empty ) {
								//                           MessageBox.Show("设置状态错误，请重试");
							} else {
								/*将当前的电压数据委托显示在前面板上*/
								Dispatcher.Invoke( new dlgMain_vDisplayValue( Main_vDisplayValue ) , txtFrequency , frequency );
							}
							Thread.Sleep( 1000 * step_frequency_period ); ;
						}
					}
				}
			} catch {
				MessageBox.Show( "请检查程控交流电源使用的串口是否正常" , "故障报警" );
			}
		}

		private void BtnMainopowerWork_Click( object sender , RoutedEventArgs e )
		{
			/*执行电瞬变实验的设置或者跳出电瞬变实验*/
			if ( btnMainopowerWork.Content.ToString( ) == "确定开始" ) {
				decimal work_keep_time = 0, diswork_keep_time = 0;
				Int32 target_count = 0;
				try {
					work_keep_time = Convert.ToDecimal( txtWorkingKeepTime.Text );
					diswork_keep_time = Convert.ToDecimal( txtDisworkKeepTime.Text );
					target_count = Convert.ToInt32( txtWorkedCountTarget.Text );
				} catch {
					MessageBox.Show( "所需要的关键参数输入不全，请补全信息" , "异常提示" );
					return;
				}

				/*需要在新线程中进行对程控电源的控制*/
				if ( trdMainpowerChanging != null ) {
					if ( trdMainpowerChanging.ThreadState != ThreadState.Stopped ) { return; }
				}

				if ( trdMainpowerChanging == null ) {
					trdMainpowerChanging = new Thread( () => Main_vMainpowerChanging( work_keep_time , diswork_keep_time , target_count ) ) {
						Name = "电瞬变试验线程" ,
						Priority = ThreadPriority.AboveNormal ,
						IsBackground = true
					};
					trdMainpowerChanging.SetApartmentState( ApartmentState.STA );
				} else {
					trdMainpowerChanging = new Thread( () => Main_vMainpowerChanging( work_keep_time , diswork_keep_time , target_count ) );
				}
				ctsExitMainpowerSwitch = new CancellationTokenSource( );
				trdMainpowerChanging.Start( );
				btnMainopowerWork.Content = "退出电瞬变";
				btnMainopowerWork.Background = Brushes.Green;
			} else {
				btnMainopowerWork.Content = "确定开始";
				btnMainopowerWork.Background = Brushes.DarkGray;
				ctsExitMainpowerSwitch.Cancel( );
			}
		}

		private void Main_vMainpowerChanging( decimal work_keep_time , decimal diswork_keep_time , Int32 target_count )
		{
			string error_information = string.Empty;
			try {
				using ( AN97002H acpower = new AN97002H( ) ) {
					for ( Int32 count = 0 ; count < target_count ; count++ ) {
						if ( !ctsExitMainpowerSwitch.IsCancellationRequested ) {
							error_information = acpower.ACPower_vControlStart( 12 , ref sp_acpower );
							//                    if (error_information != string.Empty) { MessageBox.Show("设置状态错误，请重试"); return; }
							Thread.Sleep( Convert.ToInt32( work_keep_time * 1000 ) );
							error_information = acpower.ACPower_vControlStop( 12 , ref sp_acpower );
							Thread.Sleep( Convert.ToInt32( diswork_keep_time * 1000 ) );
							Dispatcher.Invoke( new dlgMain_vDisplayValue( Main_vDisplayValue ) , txtWorkedCount , ( ushort ) ( ( count + 1 ) * 10 ) );
						} else {
							break;
						}
					}
				}
			} catch {
				MessageBox.Show( "请检查程控交流电源使用的串口是否正常" , "故障报警" );
			}
		}

		/// <summary>
		/// 窗口关闭则需要关闭工作线程
		/// </summary>
		/// <param name="sender"></param>
		/// <param name="e"></param>
		private void Window_Closed( object sender , EventArgs e )
		{
			if ( trdACPowerWorking != null ) {
				if ( trdACPowerWorking.ThreadState != ThreadState.Stopped ) { trdACPowerWorking.Abort( ); }
			}
			if ( trdMainpowerChanging != null ) {
				if ( trdMainpowerChanging.ThreadState != ThreadState.Stopped ) { trdMainpowerChanging.Abort( ); }
			}
			if ( trdCalibration != null ) {
				if ( trdCalibration.ThreadState != ThreadState.Stopped ) { trdCalibration.Abort( ); }
			}
			trdACPowerWorking = null;
			trdMainpowerChanging = null;
			trdCalibration = null;

			/*使用到的串口资源释放*/
			if ( sp_acpower != null ) { sp_acpower.Close( ); }
			if ( sp_common != null ) { sp_common.Close( ); }
			if ( sp_product != null ) { sp_product.Close( ); }
		}

		private void BtnStartCalibration_Click( object sender , RoutedEventArgs e )
		{
			try {
				if ( cobSpeciesProduct.SelectedIndex >= 0 ) {
					/*需要使用的串口不可以相同*/
					if ( ( sp_acpower == null ) || ( sp_common == null ) || ( sp_product == null ) ) {
						MessageBox.Show( "请保证使用3个不同的串口" );
						power_value  = false;
						return;
					}
					if ( ( sp_acpower.PortName == sp_common.PortName ) || ( sp_acpower.PortName == sp_product.PortName ) || ( sp_common.PortName == sp_product.PortName ) ) {
						MessageBox.Show( "请保证使用3个不同的串口" );
						power_value = false;
						return;
					}
					/*需要保证使用到的串口都可以正常工作*/
					try {
						if ( !sp_acpower.IsOpen ) { sp_acpower.Open( ); }
						sp_common.Open( );
						sp_product.Open( );
					} catch {
						MessageBox.Show( "请保证使用到的串口没有被其他程序占用" , "串口使用故障" , MessageBoxButton.OK , MessageBoxImage.Error );
						sp_acpower.Close( );
						sp_common.Close( );
						sp_product.Close( );
						power_value = false;
						return;
					}

					sp_acpower.Close( );
					sp_common.Close( );
					sp_product.Close( );

					/*若是电流的分配信息不全或者分配电流不匹配，则本次点击结果无效 并告知操作人员进行提示*/
					try {
						/*确定负载电流分配情况*/
						RadioButton rdb;
						for ( int index = 0 ; index < 3 ; index++ ) {
							rdb = grdLoad1Channel.Children[ index ] as RadioButton;
							if ( rdb.IsChecked == true ) {
								LoadChannel[ 0 ] = index + 1;
							}
						}
						for ( int index = 0 ; index < 3 ; index++ ) {
							rdb = grdLoad2Channel.Children[ index ] as RadioButton;
							if ( rdb.IsChecked == true ) {
								LoadChannel[ 1 ] = index + 1;
							}
						}
						for ( int index = 0 ; index < 3 ; index++ ) {
							rdb = grdLoad3Channel.Children[ index ] as RadioButton;
							if ( rdb.IsChecked == true ) {
								LoadChannel[ 2 ] = index + 1;
							}
						}
						/*验证电流分配值与设计值是否相同*/
						decimal current_1 = 0m, current_2 = 0m, current_3 = 0m;
						for ( int index = 0 ; index < 3 ; index++ ) {
							if ( UseLoad[ index ] ) {
								TextBox txt = grdLoadCurrent.Children[ index ] as TextBox;

								if ( LoadChannel[ index ] == 1 ) {
									current_1 += Convert.ToDecimal( txt.Text );
								} else if ( LoadChannel[ index ] == 2 ) {
									current_2 += Convert.ToDecimal( txt.Text );
								} else if ( LoadChannel[ index ] == 3 ) {
									current_3 += Convert.ToDecimal( txt.Text );
								}
							}
						}

						if ( ( current_1 != product.calibration_current_1 ) || ( current_2 != product.calibration_current_2 ) || ( current_3 != product.calibration_current_3 ) ) {
							MessageBox.Show( "请检查自定义分配的负载电流；当前分配电流值与产品设计值不匹配" , "故障报警" );
							return;
						}
					} catch {
						MessageBox.Show( "请检查自定义分配的负载电流；请保证有正确的电流值写入对应文本框" , "故障报警" );
						return;
					}

					/*实际选择到了不同的电源产品，需要将电源产品的信息进行刷新到电源的过程操作*/
					if ( trdCalibration == null ) {
						trdCalibration = new Thread( () => Main_vCalibrate( product ) ) {
							Name = "自动校准通讯控制线程" ,
							Priority = ThreadPriority.AboveNormal ,
							IsBackground = true
						};
						trdCalibration.SetApartmentState( ApartmentState.STA );
					} else {
						if ( trdCalibration.ThreadState != ThreadState.Stopped ) { return; }
						trdCalibration = new Thread( () => Main_vCalibrate( product ) );
					}

					gpbGpibAddress.IsEnabled = false;
					gpbLoadCurrent.IsEnabled = false;
					gpbOutputChannel.IsEnabled = false;
					trdCalibration.Start( );
				}
			} catch {
				MessageBox.Show( "请检查电源产品及电子负载使用的串口是否正常" , "故障报警" );
			}
		}

		Product product = new Product( );

		/*更换待进行校准的产品类型*/
		private void CobSpeciesProduct_SelectionChanged( object sender , SelectionChangedEventArgs e )
		{
			if ( cobSpeciesProduct.SelectedIndex < 0 ) { return; }
			switch ( ( Product.SpeciesName ) cobSpeciesProduct.SelectedIndex ) {
				case Product.SpeciesName.IG_B1031F__IG_B03S01:
					/*海湾单路3A电源*/
					product.calibration_current_1 = 3000; product.calibration_current_2 = 0; product.calibration_current_3 = 0;
					product.desinged_ocp_1 = 3500; product.desinged_ocp_2 = 0; product.desinged_ocp_3 = 0;
					product.species_name = Product.SpeciesName.IG_B1031F__IG_B03S01; product.communicate_baudrate = 4800;
					product.output_channel = 1; product.mainpower_undervoltage_should_calibrate = false;
					product.mainpower_voltage_should_calirate = false;product.serial_parity = Parity.None;

					chkOutput1.IsChecked = false; chkOutput2.IsChecked = true; chkOutput3.IsChecked = true;
					chkOutput1.IsChecked = true; chkOutput2.IsChecked = false; chkOutput3.IsChecked = false;
					break;
				case Product.SpeciesName.IG_X1032F:
					/*3A箱式电源*/
					product.calibration_current_1 = 3000; product.calibration_current_2 = 0; product.calibration_current_3 = 0;
					product.desinged_ocp_1 = 3500; product.desinged_ocp_2 = 0; product.desinged_ocp_3 = 0;
					product.species_name = Product.SpeciesName.IG_X1032F; product.communicate_baudrate = 4800;
					product.output_channel = 1; product.mainpower_undervoltage_should_calibrate = false;
					product.mainpower_voltage_should_calirate = false; product.serial_parity = Parity.None;

					chkOutput1.IsChecked = false; chkOutput2.IsChecked = true; chkOutput3.IsChecked = true;
					chkOutput1.IsChecked = true; chkOutput2.IsChecked = false; chkOutput3.IsChecked = false;
					break;
				case Product.SpeciesName.IG_X1041F:
					/*海湾箱式5A电源*/
					product.calibration_current_1 = 4000; product.calibration_current_2 = 0; product.calibration_current_3 = 0;
					product.desinged_ocp_1 = 5500; product.desinged_ocp_2 = 0; product.desinged_ocp_3 = 0;
					product.species_name = Product.SpeciesName.IG_X1041F; product.communicate_baudrate = 4800;
					product.output_channel = 1; product.mainpower_undervoltage_should_calibrate = false;
					product.mainpower_voltage_should_calirate = false; product.serial_parity = Parity.None;

					chkOutput1.IsChecked = false; chkOutput2.IsChecked = true; chkOutput3.IsChecked = true;
					chkOutput1.IsChecked = true; chkOutput2.IsChecked = false; chkOutput3.IsChecked = false;
					break;
				case Product.SpeciesName.IG_B1061F__IG_B06S01:
					/*海湾壁挂式6A电源*/
					product.calibration_current_1 = 6000; product.calibration_current_2 = 0; product.calibration_current_3 = 0;
					product.desinged_ocp_1 = 7800; product.desinged_ocp_2 = 0; product.desinged_ocp_3 = 0;
					product.species_name = Product.SpeciesName.IG_B1061F__IG_B06S01; product.communicate_baudrate = 4800;
					product.output_channel = 1; product.mainpower_undervoltage_should_calibrate = false;
					product.mainpower_voltage_should_calirate = false; product.serial_parity = Parity.None;

					chkOutput1.IsChecked = false; chkOutput2.IsChecked = true; chkOutput3.IsChecked = true;
					chkOutput1.IsChecked = true; chkOutput2.IsChecked = false; chkOutput3.IsChecked = false;
					break;
				case Product.SpeciesName.IG_B1061H:
					product.calibration_current_1 = 6000; product.calibration_current_2 = 0; product.calibration_current_3 = 0;
					product.desinged_ocp_1 = 7800; product.desinged_ocp_2 = 0; product.desinged_ocp_3 = 0;
					product.species_name = Product.SpeciesName.IG_B1061H; product.communicate_baudrate = 4800;
					product.output_channel = 1; product.mainpower_undervoltage_should_calibrate = false;
					product.mainpower_voltage_should_calirate = false; product.serial_parity = Parity.None;

					chkOutput1.IsChecked = false; chkOutput2.IsChecked = true; chkOutput3.IsChecked = true;
					chkOutput1.IsChecked = true; chkOutput2.IsChecked = false; chkOutput3.IsChecked = false;
					break;
				case Product.SpeciesName.IG_X1061F:
					/*海湾箱式10A电源  DY200*/
					product.calibration_current_1 = 8000; product.calibration_current_2 = 0; product.calibration_current_3 = 0;
					product.desinged_ocp_1 = 8500; product.desinged_ocp_2 = 0; product.desinged_ocp_3 = 0;
					product.species_name = Product.SpeciesName.IG_X1061F; product.communicate_baudrate = 4800;

					product.output_channel = 1; product.mainpower_undervoltage_should_calibrate = true;
					product.mainpower_voltage_should_calirate = false; product.serial_parity = Parity.None;
					chkOutput1.IsChecked = false; chkOutput2.IsChecked = true; chkOutput3.IsChecked = true;
					chkOutput1.IsChecked = true; chkOutput2.IsChecked = false; chkOutput3.IsChecked = false;
					break;
				case Product.SpeciesName.IG_X1101F:
				case Product.SpeciesName.IG_X1101H:
					/*海湾箱式10A电源*/
					product.calibration_current_1 = 10000; product.calibration_current_2 = 0; product.calibration_current_3 = 0;
					product.desinged_ocp_1 = 10500; product.desinged_ocp_2 = 0; product.desinged_ocp_3 = 0;
					product.species_name = ( Product.SpeciesName ) cobSpeciesProduct.SelectedIndex ; product.communicate_baudrate = 4800;
					product.output_channel = 1; product.mainpower_undervoltage_should_calibrate = true;
					product.mainpower_voltage_should_calirate = false; product.serial_parity = Parity.None;

					chkOutput1.IsChecked = false; chkOutput2.IsChecked = true; chkOutput3.IsChecked = true;
					chkOutput1.IsChecked = true; chkOutput2.IsChecked = false; chkOutput3.IsChecked = false;
					break;
				case Product.SpeciesName.IG_X1201F:
					/*海湾箱式20A电源*/
					product.calibration_current_1 = 20000; product.calibration_current_2 = 0; product.calibration_current_3 = 0;
					product.desinged_ocp_1 = 20500; product.desinged_ocp_2 = 0; product.desinged_ocp_3 = 0;
					product.species_name = Product.SpeciesName.IG_X1201F; product.communicate_baudrate = 4800;
					product.output_channel = 1; product.mainpower_undervoltage_should_calibrate = true;
					product.mainpower_voltage_should_calirate = false; product.serial_parity = Parity.None;

					chkOutput1.IsChecked = false; chkOutput2.IsChecked = true; chkOutput3.IsChecked = true;
					chkOutput1.IsChecked = true; chkOutput2.IsChecked = false; chkOutput3.IsChecked = false;
					break;
				case Product.SpeciesName.IG_X1301F:
					/*海湾箱式30A电源*/
					product.calibration_current_1 = 30000; product.calibration_current_2 = 0; product.calibration_current_3 = 0;
					product.desinged_ocp_1 = 31500; product.desinged_ocp_2 = 0; product.desinged_ocp_3 = 0;
					product.species_name = Product.SpeciesName.IG_X1301F; product.communicate_baudrate = 4800;
					product.output_channel = 1; product.mainpower_undervoltage_should_calibrate = true;
					product.mainpower_voltage_should_calirate = false; product.serial_parity = Parity.None;

					chkOutput1.IsChecked = false; chkOutput2.IsChecked = true; chkOutput3.IsChecked = true;
					chkOutput1.IsChecked = true; chkOutput2.IsChecked = false; chkOutput3.IsChecked = false;
					break;
				case Product.SpeciesName.IG_B2031F__IG_B03D01:
					/*依爱3A双路电源*/
					product.calibration_current_1 = 2000; product.calibration_current_2 = 1000; product.calibration_current_3 = 0;
					product.desinged_ocp_1 = 4000; product.desinged_ocp_2 = 1500; product.desinged_ocp_3 = 0;
					product.species_name = Product.SpeciesName.IG_B2031F__IG_B03D01; product.communicate_baudrate = 4800;
					product.output_channel = 1; product.mainpower_undervoltage_should_calibrate = false;
					product.mainpower_voltage_should_calirate = false; product.serial_parity = Parity.None;

					chkOutput1.IsChecked = false; chkOutput2.IsChecked = false; chkOutput3.IsChecked = true;
					chkOutput1.IsChecked = true; chkOutput2.IsChecked = true; chkOutput3.IsChecked = false;
					break;
				case Product.SpeciesName.IG_B2031G__IG_B03D02:
					/**/
					product.calibration_current_1 = 2000; product.calibration_current_2 = 1000; product.calibration_current_3 = 0;
					product.desinged_ocp_1 = 4000; product.desinged_ocp_2 = 1500; product.desinged_ocp_3 = 0;
					product.species_name = Product.SpeciesName.IG_B2031G__IG_B03D02; product.communicate_baudrate = 4800;
					product.output_channel = 1; product.mainpower_undervoltage_should_calibrate = false;
					product.mainpower_voltage_should_calirate = false; product.serial_parity = Parity.None;

					chkOutput1.IsChecked = false; chkOutput2.IsChecked = false; chkOutput3.IsChecked = true;
					chkOutput1.IsChecked = true; chkOutput2.IsChecked = true; chkOutput3.IsChecked = false;
					break;
				case Product.SpeciesName.IG_B2031H__IG_B03D03:
					/**/
					product.calibration_current_1 = 2000; product.calibration_current_2 = 1000; product.calibration_current_3 = 0;
					product.desinged_ocp_1 = 4000; product.desinged_ocp_2 = 1500; product.desinged_ocp_3 = 0;
					product.species_name = Product.SpeciesName.IG_B2031H__IG_B03D03; product.communicate_baudrate = 4800;
					product.output_channel = 1; product.mainpower_undervoltage_should_calibrate = false;
					product.mainpower_voltage_should_calirate = false; product.serial_parity = Parity.None;

					chkOutput1.IsChecked = false; chkOutput2.IsChecked = false; chkOutput3.IsChecked = true;
					chkOutput1.IsChecked = true; chkOutput2.IsChecked = true; chkOutput3.IsChecked = false;
					break;
				case Product.SpeciesName.IG_B2022F:
					/*24V 2A电源*/
					product.calibration_current_1 = 1500; product.calibration_current_2 = 0; product.calibration_current_3 = 0;
					product.desinged_ocp_1 = 2200; product.desinged_ocp_2 = 0; product.desinged_ocp_3 = 0;
					product.species_name = Product.SpeciesName.IG_B2022F; product.communicate_baudrate = 115200;
					product.output_channel = 1; product.mainpower_undervoltage_should_calibrate = false;
					product.mainpower_voltage_should_calirate = false; product.serial_parity = Parity.None;

					chkOutput1.IsChecked = false; chkOutput2.IsChecked = true; chkOutput3.IsChecked = true;
					chkOutput1.IsChecked = true; chkOutput2.IsChecked = false; chkOutput3.IsChecked = false;
					break;
				case Product.SpeciesName.IG_B1032F:
					/*12V 3A单路电源，可手动主备电转换，无辅助通道关段控制*/
					product.calibration_current_1 = 3000; product.calibration_current_2 = 0; product.calibration_current_3 = 0;
					product.desinged_ocp_1 = 3500; product.desinged_ocp_2 = 0; product.desinged_ocp_3 = 0;
					product.species_name = Product.SpeciesName.IG_B1032F; product.communicate_baudrate = 115200;
					product.output_channel = 1; product.mainpower_undervoltage_should_calibrate = false;
					product.mainpower_voltage_should_calirate = false; product.serial_parity = Parity.None;

					chkOutput1.IsChecked = false; chkOutput2.IsChecked = true; chkOutput3.IsChecked = true;
					chkOutput1.IsChecked = true; chkOutput2.IsChecked = false; chkOutput3.IsChecked = false;
					break;
				case Product.SpeciesName.IG_B2032F:
					/*12V 3A电源*/
					product.calibration_current_1 = 0; product.calibration_current_2 = 2000; product.calibration_current_3 = 0;
					product.desinged_ocp_1 = 0; product.desinged_ocp_2 = 4000; product.desinged_ocp_3 = 0;
					product.species_name = Product.SpeciesName.IG_B2032F; product.communicate_baudrate = 115200;
					product.output_channel = 2; product.mainpower_undervoltage_should_calibrate = false;
					product.mainpower_voltage_should_calirate = false; product.serial_parity = Parity.None;

					chkOutput1.IsChecked = false; chkOutput2.IsChecked = false; chkOutput3.IsChecked = true;
					chkOutput1.IsChecked = true; chkOutput2.IsChecked = true; chkOutput3.IsChecked = false;
					break;
				case Product.SpeciesName.IG_B2032H:
					/*声讯12V 3A电源*/
					product.calibration_current_1 = 0; product.calibration_current_2 = 2000; product.calibration_current_3 = 0;
					product.desinged_ocp_1 = 0; product.desinged_ocp_2 = 4000; product.desinged_ocp_3 = 0;
					product.species_name = Product.SpeciesName.IG_B2032H; product.communicate_baudrate = 115200;
					product.output_channel = 2; product.mainpower_undervoltage_should_calibrate = false;
					product.mainpower_voltage_should_calirate = false; product.serial_parity = Parity.None;

					chkOutput1.IsChecked = false; chkOutput2.IsChecked = false; chkOutput3.IsChecked = true;
					chkOutput1.IsChecked = true; chkOutput2.IsChecked = true; chkOutput3.IsChecked = false;
					break;
				case Product.SpeciesName.IG_B2053F:
				case Product.SpeciesName.IG_B2053H:				
					/*兼容2055电源  安装时需要将第二路认为是第一路*/
					product.calibration_current_1 = 3000; product.calibration_current_2 = 0; product.calibration_current_3 = 0;
					product.desinged_ocp_1 = 3150; product.desinged_ocp_2 = 0; product.desinged_ocp_3 = 0;
					product.species_name = ( Product.SpeciesName ) cobSpeciesProduct.SelectedIndex; product.communicate_baudrate = 9600;
					product.output_channel = 1; product.mainpower_undervoltage_should_calibrate = false;
					product.mainpower_voltage_should_calirate = false; product.serial_parity = Parity.None;

					chkOutput1.IsChecked = false; chkOutput2.IsChecked = true; chkOutput3.IsChecked = true;
					chkOutput1.IsChecked = true; chkOutput2.IsChecked = false; chkOutput3.IsChecked = false;
					break;
				case Product.SpeciesName.IG_B2053K:
					/*27.5V - 4A （软件控制）    5V-2A(硬件控制)*/
					product.calibration_current_1 = 4000; product.calibration_current_2 = 0; product.calibration_current_3 = 0;
					product.desinged_ocp_1 = 4500; product.desinged_ocp_2 = 0; product.desinged_ocp_3 = 0;
					product.species_name = ( Product.SpeciesName )cobSpeciesProduct.SelectedIndex; product.communicate_baudrate = 9600;
					product.output_channel = 1; product.mainpower_undervoltage_should_calibrate = false;
					product.mainpower_voltage_should_calirate = false; product.serial_parity = Parity.None;

					chkOutput1.IsChecked = false; chkOutput2.IsChecked = true; chkOutput3.IsChecked = true;
					chkOutput1.IsChecked = true; chkOutput2.IsChecked = false; chkOutput3.IsChecked = false;
					break;
				case Product.SpeciesName.IG_B2073F:
					/*兼容2055电源  安装时需要将第二路认为是第一路*/
					product.calibration_current_1 = 6000; product.calibration_current_2 = 0; product.calibration_current_3 = 0;
					product.desinged_ocp_1 = 6500; product.desinged_ocp_2 = 0; product.desinged_ocp_3 = 0;
					product.species_name = Product.SpeciesName.IG_B2073F; product.communicate_baudrate = 9600;
					product.output_channel = 1; product.mainpower_undervoltage_should_calibrate = false;
					product.mainpower_voltage_should_calirate = false; product.serial_parity = Parity.None;

					chkOutput1.IsChecked = false; chkOutput2.IsChecked = true; chkOutput3.IsChecked = true;
					chkOutput1.IsChecked = true; chkOutput2.IsChecked = false; chkOutput3.IsChecked = false;
					break;
				case Product.SpeciesName.IG_B1051H:
					/*IG-B2073F改*/
					product.calibration_current_1 = 5000; product.calibration_current_2 = 0; product.calibration_current_3 = 0;
					product.desinged_ocp_1 = 5500; product.desinged_ocp_2 = 0; product.desinged_ocp_3 = 0;
					product.species_name = Product.SpeciesName.IG_B1051H; product.communicate_baudrate = 9600;
					product.output_channel = 1; product.mainpower_undervoltage_should_calibrate = false;
					product.mainpower_voltage_should_calirate = false; product.serial_parity = Parity.None;

					chkOutput1.IsChecked = false; chkOutput2.IsChecked = true; chkOutput3.IsChecked = true;
					chkOutput1.IsChecked = true; chkOutput2.IsChecked = false; chkOutput3.IsChecked = false;
					break;
				case Product.SpeciesName.IG_M1101F__IG_M10S01:
					/*依爱10A单路电源*/
					product.calibration_current_1 = 10000; product.calibration_current_2 = 0; product.calibration_current_3 = 0;
					product.desinged_ocp_1 = 12000; product.desinged_ocp_2 = 0; product.desinged_ocp_3 = 0;
					product.species_name = Product.SpeciesName.IG_M1101F__IG_M10S01; product.communicate_baudrate = 9600;
					product.output_channel = 1; product.mainpower_undervoltage_should_calibrate = true;
					product.mainpower_voltage_should_calirate = false; product.serial_parity = Parity.None;

					chkOutput1.IsChecked = false; chkOutput2.IsChecked = true; chkOutput3.IsChecked = true;
					chkOutput1.IsChecked = true; chkOutput2.IsChecked = false; chkOutput3.IsChecked = false;
					break;
				case Product.SpeciesName.IG_M1101H:/*尼特1U单路10A电源*/
					product.calibration_current_1 = 10000; product.calibration_current_2 = 0; product.calibration_current_3 = 0;
					product.desinged_ocp_1 = 10500; product.desinged_ocp_2 = 0; product.desinged_ocp_3 = 0;
					product.species_name = ( Product.SpeciesName )cobSpeciesProduct.SelectedIndex; product.communicate_baudrate = 2400;
					product.output_channel = 1; product.mainpower_undervoltage_should_calibrate = true;
					product.mainpower_voltage_should_calirate = false; product.serial_parity = Parity.None;

					chkOutput1.IsChecked = false; chkOutput2.IsChecked = true; chkOutput3.IsChecked = true;
					chkOutput1.IsChecked = true; chkOutput2.IsChecked = false; chkOutput3.IsChecked = false;
					break;
				case Product.SpeciesName.IG_M1102F:/*标准1U单路10A电源*/
				case Product.SpeciesName.IG_X1101K: /*泰和安10A箱式电源*/
					product.calibration_current_1 = 10000; product.calibration_current_2 = 0; product.calibration_current_3 = 0;
					product.desinged_ocp_1 = 10500; product.desinged_ocp_2 = 0; product.desinged_ocp_3 = 0;
					product.species_name = ( Product.SpeciesName ) cobSpeciesProduct.SelectedIndex ; product.communicate_baudrate = 9600;
					product.output_channel = 1; product.mainpower_undervoltage_should_calibrate = true;
					product.mainpower_voltage_should_calirate = false; product.serial_parity = Parity.None;

					chkOutput1.IsChecked = false; chkOutput2.IsChecked = true; chkOutput3.IsChecked = true;
					chkOutput1.IsChecked = true; chkOutput2.IsChecked = false; chkOutput3.IsChecked = false;
					break;
				case Product.SpeciesName.IG_M1202F:
					/*标准1U单路20A电源*/
					product.calibration_current_1 = 20000; product.calibration_current_2 = 0; product.calibration_current_3 = 0;
					product.desinged_ocp_1 = 21000; product.desinged_ocp_2 = 0; product.desinged_ocp_3 = 0;
					product.species_name = Product.SpeciesName.IG_M1202F; product.communicate_baudrate = 9600;
					product.output_channel = 1; product.mainpower_undervoltage_should_calibrate = true;
					product.mainpower_voltage_should_calirate = false; product.serial_parity = Parity.None;

					chkOutput1.IsChecked = false; chkOutput2.IsChecked = true; chkOutput3.IsChecked = true;
					chkOutput1.IsChecked = true; chkOutput2.IsChecked = false; chkOutput3.IsChecked = false;
					break;
				case Product.SpeciesName.IG_M1302F:
					/*标准I1U单路30A电源*/
					product.calibration_current_1 = 30000; product.calibration_current_2 = 0; product.calibration_current_3 = 0;
					product.desinged_ocp_1 = 31500; product.desinged_ocp_2 = 0; product.desinged_ocp_3 = 0;
					product.species_name = Product.SpeciesName.IG_M1302F; product.communicate_baudrate = 9600;
					product.output_channel = 1; product.mainpower_undervoltage_should_calibrate = true;
					product.mainpower_voltage_should_calirate = false; product.serial_parity = Parity.None;

					chkOutput1.IsChecked = false; chkOutput2.IsChecked = true; chkOutput3.IsChecked = true;
					chkOutput1.IsChecked = true; chkOutput2.IsChecked = false; chkOutput3.IsChecked = false;
					break;
				case Product.SpeciesName.IG_M1102H:
					/*泛海三江  1U电源改10A单路输出*/
					product.calibration_current_1 = 8000; product.calibration_current_2 = 0; product.calibration_current_3 = 0;
					product.desinged_ocp_1 = 10500; product.desinged_ocp_2 = 0; product.desinged_ocp_3 = 0;
					product.species_name = Product.SpeciesName.IG_M1102H; product.communicate_baudrate = 4800;
					product.output_channel = 1; product.mainpower_undervoltage_should_calibrate = true;
					product.mainpower_voltage_should_calirate = false; product.serial_parity = Parity.None;

					chkOutput1.IsChecked = false; chkOutput2.IsChecked = true; chkOutput3.IsChecked = true;
					chkOutput1.IsChecked = true; chkOutput2.IsChecked = false; chkOutput3.IsChecked = false;
					break;
				case Product.SpeciesName.IG_M1202H:
					/*泛海三江  1U电源改20A单路输出 - 硬件电路限制，电流采样还是使用的两个通道，在校准时认为此电源存在2路输出，过流点都设置为较高的21A*/
					product.calibration_current_1 = 8000; product.calibration_current_2 = 8000; product.calibration_current_3 = 0;
					product.desinged_ocp_1 = 21000; product.desinged_ocp_2 = 20500; product.desinged_ocp_3 = 0;
					product.species_name = Product.SpeciesName.IG_M1202H; product.communicate_baudrate = 4800;
					product.output_channel = 2; product.mainpower_undervoltage_should_calibrate = true;
					product.mainpower_voltage_should_calirate = false; product.serial_parity = Parity.None;

					chkOutput1.IsChecked = false; chkOutput2.IsChecked = false; chkOutput3.IsChecked = true;
					chkOutput1.IsChecked = true; chkOutput2.IsChecked = true; chkOutput3.IsChecked = false;
					break;
				//case Product.SpeciesName.IG_M1302F: //之前发送给泰和安   现在已更改
				//	/*1U电源改30A单路输出 - 硬件电路限制，电流采样还是使用的3个通道，在校准时认为此电源存在3路输出，过流点都设置为较高的31.5A*/
				//	product.calibration_current_1 = 8000; product.calibration_current_2 = 8000; product.calibration_current_3 = 8000;
				//	product.desinged_ocp_1 = 31500; product.desinged_ocp_2 = 30500; product.desinged_ocp_3 = 30500;
				//	product.species_name = Product.SpeciesName.IG_M1302F; product.communicate_baudrate = 4800;
				//	product.output_channel = 3; product.mainpower_undervoltage_should_calibrate = true;
				//	product.mainpower_voltage_should_calirate = false;

				//	chkOutput1.IsChecked = false; chkOutput2.IsChecked = false; chkOutput3.IsChecked = false;
				//	chkOutput1.IsChecked = true; chkOutput2.IsChecked = true; chkOutput3.IsChecked = true;
				//	break;
				case Product.SpeciesName.J_EI8212:
					/*依爱10A双路隔离电源*/
					product.calibration_current_1 = 4000; product.calibration_current_2 = 6000; product.calibration_current_3 = 0;
					product.desinged_ocp_1 = 6000; product.desinged_ocp_2 = 7200; product.desinged_ocp_3 = 0;
					product.species_name = Product.SpeciesName.J_EI8212; product.communicate_baudrate = 9600;
					product.output_channel = 2; product.mainpower_undervoltage_should_calibrate = true;
					product.mainpower_voltage_should_calirate = false; product.serial_parity = Parity.None;

					chkOutput1.IsChecked = false; chkOutput2.IsChecked = false; chkOutput3.IsChecked = true;
					chkOutput1.IsChecked = true; chkOutput2.IsChecked = true; chkOutput3.IsChecked = false;
					break;
				case Product.SpeciesName.IG_M2131H:
					/*法安通 5A和8A电源*/
					product.calibration_current_1 = 4000; product.calibration_current_2 = 6000; product.calibration_current_3 = 0;
					product.desinged_ocp_1 = 5500; product.desinged_ocp_2 = 8500; product.desinged_ocp_3 = 0;
					product.species_name = Product.SpeciesName.IG_M2131H; product.communicate_baudrate = 19200;
					product.output_channel = 2; product.mainpower_undervoltage_should_calibrate = true;
					product.mainpower_voltage_should_calirate = false; product.serial_parity = Parity.None;

					chkOutput1.IsChecked = false; chkOutput2.IsChecked = false; chkOutput3.IsChecked = true;
					chkOutput1.IsChecked = true; chkOutput2.IsChecked = true; chkOutput3.IsChecked = false;
					break;
				case Product.SpeciesName.IG_M2132F:
					/*标准 5A和8A电源*/
					product.calibration_current_1 = 5000; product.calibration_current_2 = 8000; product.calibration_current_3 = 0;
					product.desinged_ocp_1 = 5500; product.desinged_ocp_2 = 8500; product.desinged_ocp_3 = 0;
					product.species_name = Product.SpeciesName.IG_M2132F; product.communicate_baudrate = 9600;
					product.output_channel = 2; product.mainpower_undervoltage_should_calibrate = true;
					product.mainpower_voltage_should_calirate = false; product.serial_parity = Parity.None;

					chkOutput1.IsChecked = false; chkOutput2.IsChecked = false; chkOutput3.IsChecked = true;
					chkOutput1.IsChecked = true; chkOutput2.IsChecked = true; chkOutput3.IsChecked = false;
					break;
				case Product.SpeciesName.GST_LD_D02H:				
					/*海湾10A双路电源 - D02*/
					product.calibration_current_1 = 2000; product.calibration_current_2 = 8000; product.calibration_current_3 = 0;
					product.desinged_ocp_1 = 2500; product.desinged_ocp_2 = 10000; product.desinged_ocp_3 = 0;
					product.species_name = Product.SpeciesName.GST_LD_D02H; product.communicate_baudrate = 4800;
					product.output_channel = 2; product.mainpower_undervoltage_should_calibrate = true;
					product.mainpower_voltage_should_calirate = false; product.serial_parity = Parity.None;

					chkOutput1.IsChecked = false; chkOutput2.IsChecked = false; chkOutput3.IsChecked = true;
					chkOutput1.IsChecked = true; chkOutput2.IsChecked = true; chkOutput3.IsChecked = false;
					break;
				case Product.SpeciesName.IG_M2121F:
					/*海湾10A双路电源 - D02改   第二路标准10A*/
					product.calibration_current_1 = 2000; product.calibration_current_2 = 10000; product.calibration_current_3 = 0;
					product.desinged_ocp_1 = 2500; product.desinged_ocp_2 = 12000; product.desinged_ocp_3 = 0;
					product.species_name = Product.SpeciesName.IG_M2121F; product.communicate_baudrate = 4800;
					product.output_channel = 2; product.mainpower_undervoltage_should_calibrate = true;
					product.mainpower_voltage_should_calirate = false; product.serial_parity = Parity.None;

					chkOutput1.IsChecked = false; chkOutput2.IsChecked = false; chkOutput3.IsChecked = true;
					chkOutput1.IsChecked = true; chkOutput2.IsChecked = true; chkOutput3.IsChecked = false;
					break;
				case Product.SpeciesName.IG_B2108:
					/*海湾10A双路电源 改IG-B2108*/
					product.calibration_current_1 = 5000; product.calibration_current_2 = 5000; product.calibration_current_3 = 0;
					product.desinged_ocp_1 = 6000; product.desinged_ocp_2 = 6000; product.desinged_ocp_3 = 0;
					product.species_name = Product.SpeciesName.IG_B2108; product.communicate_baudrate = 4800;
					product.output_channel = 2; product.mainpower_undervoltage_should_calibrate = true;
					product.mainpower_voltage_should_calirate = false; product.serial_parity = Parity.None;

					chkOutput1.IsChecked = false; chkOutput2.IsChecked = false; chkOutput3.IsChecked = true;
					chkOutput1.IsChecked = true; chkOutput2.IsChecked = true; chkOutput3.IsChecked = false;
					break;
				case Product.SpeciesName.IG_M2102F:
					/*常规型号 2102F*/
					product.calibration_current_1 = 4000; product.calibration_current_2 = 6000; product.calibration_current_3 = 0;
					product.desinged_ocp_1 = 4500; product.desinged_ocp_2 = 6500; product.desinged_ocp_3 = 0;
					product.species_name = Product.SpeciesName.IG_M2102F; product.communicate_baudrate = 9600;
					product.output_channel = 2; product.mainpower_undervoltage_should_calibrate = true;
					product.mainpower_voltage_should_calirate = false; product.serial_parity = Parity.None;

					chkOutput1.IsChecked = false; chkOutput2.IsChecked = false; chkOutput3.IsChecked = true;
					chkOutput1.IsChecked = true; chkOutput2.IsChecked = true; chkOutput3.IsChecked = false;
					break;
				case Product.SpeciesName.IG_M2202F:
					/*泰和安双路20A电源*/
					product.calibration_current_1 = 10000; product.calibration_current_2 = 10000; product.calibration_current_3 = 0;
					product.desinged_ocp_1 = 10500; product.desinged_ocp_2 = 10500; product.desinged_ocp_3 = 0;
					product.species_name = Product.SpeciesName.IG_M2202F; product.communicate_baudrate = 9600;
					product.output_channel = 2; product.mainpower_undervoltage_should_calibrate = true;
					product.mainpower_voltage_should_calirate = false; product.serial_parity = Parity.None;

					chkOutput1.IsChecked = false; chkOutput2.IsChecked = false; chkOutput3.IsChecked = true;
					chkOutput1.IsChecked = true; chkOutput2.IsChecked = true; chkOutput3.IsChecked = false;
					break;
				case Product.SpeciesName.IG_M3201F__20A主机电源:
					/*海湾20A主机电源*/
					product.calibration_current_1 = 10000; product.calibration_current_2 = 2000; product.calibration_current_3 = 8000;
					product.desinged_ocp_1 = 17000; product.desinged_ocp_2 = 2500; product.desinged_ocp_3 = 12000;
					product.species_name = Product.SpeciesName.IG_M3201F__20A主机电源; product.communicate_baudrate = 4800;
					product.output_channel = 3; product.mainpower_undervoltage_should_calibrate = true;
					product.mainpower_voltage_should_calirate = false; product.serial_parity = Parity.None;

					chkOutput1.IsChecked = false; chkOutput2.IsChecked = false; chkOutput3.IsChecked = false;
					chkOutput1.IsChecked = true; chkOutput2.IsChecked = true; chkOutput3.IsChecked = true;
					break;
				case Product.SpeciesName.GST_LD_D06H:
				case Product.SpeciesName.IG_M3242F:
					/*海湾D06*/
					product.calibration_current_1 = 8000; product.calibration_current_2 = 8000; product.calibration_current_3 = 8000;
					product.desinged_ocp_1 = 12000; product.desinged_ocp_2 = 12000; product.desinged_ocp_3 = 12000;
					product.species_name = ( Product.SpeciesName ) cobSpeciesProduct.SelectedIndex;
					if ( product.species_name == Product.SpeciesName.GST_LD_D06H ) { product.communicate_baudrate = 4800; } else if ( product.species_name == Product.SpeciesName.IG_M3242F ) { product.communicate_baudrate = 9600; }
					product.output_channel = 3; product.mainpower_undervoltage_should_calibrate = true;
					product.mainpower_voltage_should_calirate = false; product.serial_parity = Parity.None;

					chkOutput1.IsChecked = false; chkOutput2.IsChecked = false; chkOutput3.IsChecked = false;
					chkOutput1.IsChecked = true; chkOutput2.IsChecked = true; chkOutput3.IsChecked = true;
					break;
				case Product.SpeciesName.IG_M3202F:
					product.calibration_current_1 = 10000; product.calibration_current_2 = 10000; product.calibration_current_3 = 10000;
					product.desinged_ocp_1 = 12500; product.desinged_ocp_2 = 12500; product.desinged_ocp_3 = 12500;
					product.species_name = ( Product.SpeciesName )cobSpeciesProduct.SelectedIndex;
					product.communicate_baudrate = 9600; 
					product.output_channel = 3; product.mainpower_undervoltage_should_calibrate = true;
					product.mainpower_voltage_should_calirate = false; product.serial_parity = Parity.Mark;

					chkOutput1.IsChecked = false; chkOutput2.IsChecked = false; chkOutput3.IsChecked = false;
					chkOutput1.IsChecked = true; chkOutput2.IsChecked = true; chkOutput3.IsChecked = true;
					break;
				case Product.SpeciesName.IG_M3302F:
					/*泰和安30A电源*/
					product.calibration_current_1 = 10000; product.calibration_current_2 = 10000; product.calibration_current_3 = 10000;
					product.desinged_ocp_1 = 10500; product.desinged_ocp_2 = 10500; product.desinged_ocp_3 = 10500;
					product.species_name = Product.SpeciesName.IG_M3302F; product.communicate_baudrate = 9600;
					product.output_channel = 3; product.mainpower_undervoltage_should_calibrate = true;
					product.mainpower_voltage_should_calirate = false; product.serial_parity = Parity.None;

					chkOutput1.IsChecked = false; chkOutput2.IsChecked = false; chkOutput3.IsChecked = false;
					chkOutput1.IsChecked = true; chkOutput2.IsChecked = true; chkOutput3.IsChecked = true;
					break;
				case Product.SpeciesName.IG_Z2071F:
					/*应急照明电源3节电池300W*/
					product.calibration_current_1 = 7000; product.calibration_current_2 = 0; product.calibration_current_3 = 0;
					product.desinged_ocp_1 = 8900; product.desinged_ocp_2 = 0; product.desinged_ocp_3 = 0;
					product.species_name = Product.SpeciesName.IG_Z2071F; product.communicate_baudrate = 9600;
					product.output_channel = 1; product.mainpower_undervoltage_should_calibrate = true;
					product.mainpower_voltage_should_calirate = true; product.serial_parity = Parity.None;

					chkOutput1.IsChecked = false; chkOutput2.IsChecked = true; chkOutput3.IsChecked = true;
					chkOutput1.IsChecked = true; chkOutput2.IsChecked = false; chkOutput3.IsChecked = false;
					break;
				case Product.SpeciesName.IG_Z2102F:
					/*应急照明电源2节电池300W*/
					product.calibration_current_1 = 10000; product.calibration_current_2 = 0; product.calibration_current_3 = 0;
					product.desinged_ocp_1 = 12500; product.desinged_ocp_2 = 0; product.desinged_ocp_3 = 0;
					product.species_name = Product.SpeciesName.IG_Z2102F; product.communicate_baudrate = 9600;
					product.output_channel = 1; product.mainpower_undervoltage_should_calibrate = true;
					product.mainpower_voltage_should_calirate = true; product.serial_parity = Parity.None;

					chkOutput1.IsChecked = false; chkOutput2.IsChecked = true; chkOutput3.IsChecked = true;
					chkOutput1.IsChecked = true; chkOutput2.IsChecked = false; chkOutput3.IsChecked = false;
					break;
				case Product.SpeciesName.IG_Z2121F:
					/*应急照明电源3节电池500W*/
					product.calibration_current_1 = 12000; product.calibration_current_2 = 0; product.calibration_current_3 = 0;
					product.desinged_ocp_1 = 14900; product.desinged_ocp_2 = 0; product.desinged_ocp_3 = 0;
					product.species_name = Product.SpeciesName.IG_Z2121F; product.communicate_baudrate = 9600;
					product.output_channel = 1; product.mainpower_undervoltage_should_calibrate = true;
					product.mainpower_voltage_should_calirate = true; product.serial_parity = Parity.None;

					chkOutput1.IsChecked = false; chkOutput2.IsChecked = true; chkOutput3.IsChecked = true;
					chkOutput1.IsChecked = true; chkOutput2.IsChecked = false; chkOutput3.IsChecked = false;
					break;
				case Product.SpeciesName.IG_Z2182F:
					/*应急照明电源2节电池500W*/
					product.calibration_current_1 = 17600; product.calibration_current_2 = 0; product.calibration_current_3 = 0;
					product.desinged_ocp_1 = 21600; product.desinged_ocp_2 = 0; product.desinged_ocp_3 = 0;
					product.species_name = Product.SpeciesName.IG_Z2182F; product.communicate_baudrate = 9600;
					product.output_channel = 1; product.mainpower_undervoltage_should_calibrate = true;
					product.mainpower_voltage_should_calirate = true; product.serial_parity = Parity.None;

					chkOutput1.IsChecked = false; chkOutput2.IsChecked = true; chkOutput3.IsChecked = true;
					chkOutput1.IsChecked = true; chkOutput2.IsChecked = false; chkOutput3.IsChecked = false;
					break;
				case Product.SpeciesName.IG_Z2181F:
					/*应急照明电源3节电池750W*/
					product.calibration_current_1 = 17700; product.calibration_current_2 = 0; product.calibration_current_3 = 0;
					product.desinged_ocp_1 = 23000; product.desinged_ocp_2 = 0; product.desinged_ocp_3 = 0;
					product.species_name = Product.SpeciesName.IG_Z2181F; product.communicate_baudrate = 9600;
					product.output_channel = 1; product.mainpower_undervoltage_should_calibrate = true;
					product.mainpower_voltage_should_calirate = true; product.serial_parity = Parity.None;

					chkOutput1.IsChecked = false; chkOutput2.IsChecked = true; chkOutput3.IsChecked = true;
					chkOutput1.IsChecked = true; chkOutput2.IsChecked = false; chkOutput3.IsChecked = false;
					break;
				case Product.SpeciesName.IG_Z2272F:
					/*应急照明电源2节电池750W*/
					product.calibration_current_1 = 26700; product.calibration_current_2 = 0; product.calibration_current_3 = 0;
					product.desinged_ocp_1 = 34500; product.desinged_ocp_2 = 0; product.desinged_ocp_3 = 0;
					product.species_name = Product.SpeciesName.IG_Z2272F; product.communicate_baudrate = 9600;
					product.output_channel = 1; product.mainpower_undervoltage_should_calibrate = true;
					product.mainpower_voltage_should_calirate = true; product.serial_parity = Parity.None;

					chkOutput1.IsChecked = false; chkOutput2.IsChecked = true; chkOutput3.IsChecked = true;
					chkOutput1.IsChecked = true; chkOutput2.IsChecked = false; chkOutput3.IsChecked = false;
					break;
				case Product.SpeciesName.IG_Z2102L:
					/*应急照明电源2节电池300W 赋安专用 取消备电单投功能*/
					product.calibration_current_1 = 10000; product.calibration_current_2 = 0; product.calibration_current_3 = 0;
					product.desinged_ocp_1 = 12500; product.desinged_ocp_2 = 0; product.desinged_ocp_3 = 0;
					product.species_name = Product.SpeciesName.IG_Z2102L; product.communicate_baudrate = 9600;
					product.output_channel = 1; product.mainpower_undervoltage_should_calibrate = true;
					product.mainpower_voltage_should_calirate = true; product.serial_parity = Parity.None;

					chkOutput1.IsChecked = false; chkOutput2.IsChecked = true; chkOutput3.IsChecked = true;
					chkOutput1.IsChecked = true; chkOutput2.IsChecked = false; chkOutput3.IsChecked = false;
					break;
				case Product.SpeciesName.IG_Z2182L:
					/*应急照明电源2节电池500W 赋安专用 取消备电单投功能*/
					product.calibration_current_1 = 17600; product.calibration_current_2 = 0; product.calibration_current_3 = 0;
					product.desinged_ocp_1 = 21600; product.desinged_ocp_2 = 0; product.desinged_ocp_3 = 0;
					product.species_name = Product.SpeciesName.IG_Z2182L; product.communicate_baudrate = 9600;
					product.output_channel = 1; product.mainpower_undervoltage_should_calibrate = true;
					product.mainpower_voltage_should_calirate = true; product.serial_parity = Parity.None;

					chkOutput1.IsChecked = false; chkOutput2.IsChecked = true; chkOutput3.IsChecked = true;
					chkOutput1.IsChecked = true; chkOutput2.IsChecked = false; chkOutput3.IsChecked = false;
					break;
				case Product.SpeciesName.IG_Z2272L:
					/*应急照明电源2节电池750W 赋安专用 取消备电单投功能*/
					product.calibration_current_1 = 26700; product.calibration_current_2 = 0; product.calibration_current_3 = 0;
					product.desinged_ocp_1 = 34500; product.desinged_ocp_2 = 0; product.desinged_ocp_3 = 0;
					product.species_name = Product.SpeciesName.IG_Z2272L; product.communicate_baudrate = 9600;
					product.output_channel = 1; product.mainpower_undervoltage_should_calibrate = true;
					product.mainpower_voltage_should_calirate = true; product.serial_parity = Parity.None;

					chkOutput1.IsChecked = false; chkOutput2.IsChecked = true; chkOutput3.IsChecked = true;
					chkOutput1.IsChecked = true; chkOutput2.IsChecked = false; chkOutput3.IsChecked = false;
					break;
				default:
					break;
			}

			//使能产品使用串口的cob选择
			cobSerialPort_Product.IsEnabled = true;

			UserSettedCurrent [ 0 ] = product.calibration_current_1; UserSettedCurrent[ 1 ] = product.calibration_current_2;
			UserSettedCurrent[ 2 ] = product.calibration_current_3; txtCurrent1.Text = UserSettedCurrent[ 0 ].ToString( );
			txtCurrent2.Text = UserSettedCurrent[ 1 ].ToString( ); txtCurrent3.Text = UserSettedCurrent[ 2 ].ToString( );

		}

		/// <summary>
		/// 按照对应的信息进行校准
		/// </summary>
		/// <param name="product">待校准产品的相关信息</param>
		void Main_vCalibrate( Product product )
		{
			string error_information = string.Empty;
			string error_information_1 = string.Empty;
			/*先执行默认的通讯设置*/
			using ( MCU_Control mcu_control = new MCU_Control( ) ) {
				using ( AN97002H an97002h = new AN97002H( ) ) {
					using (ElectronicLoad_Control_IT8500 it8500 = new ElectronicLoad_Control_IT8500()) {
						/*关闭所有的电子负载带载；关闭程控交流电源(备电无法单投的电源除外)；只允许不受控制的备电正常打开*/
						MessageBox.Show( "请保证电源正常输出", "操作提示", MessageBoxButton.OK, MessageBoxImage.Information );

						int index = 0;
						try {
							while ((error_information == string.Empty) && (index < 10)) {
								switch (index) {
									case 0:
										/*将电源的校准数据恢复到默认的状态 - 可以先进入校准模式后将校准数据所在扇区全部擦除来解决*/
										error_information = Main_vCalibration_Initalize( product, mcu_control, an97002h, it8500 );
										break;
									case 1:
										/*备电电压的校准*/
										error_information = Main_vCalibration_StandbyPower( product, mcu_control, it8500, an97002h );
										break;
									case 2:
										/*零点电流校准*/
										error_information = Main_vCalibration_CurrentZero( product, mcu_control, it8500 );
										break;
									case 3:
										/*主电欠压点校准*/
										error_information = Main_vCalibration_MainpowerUnderVoltage( product, mcu_control, an97002h, it8500 );
										break;
									case 4:
										/*主电满载校准*/
										error_information = Main_vCalibration_MainpowerFullLoad( product, mcu_control, an97002h, it8500 );
										break;
									case 5:
										/*OCP的相关设置*/
										error_information = Main_vCalibration_OCPSetting( product, mcu_control );
										break;
									case 6:
										/*备电工作状态下电流的校准*/
										error_information = Main_vCalibration_SpCurrentRatioSetting( product, mcu_control, it8500, an97002h );
										break;
									case 7:
										/*备电关断之后蜂鸣器的工作时间设置*/
										error_information = Main_vCalibration_BeepWorkingTimeSetting( product, mcu_control );
										break;
									case 8:
										/*其它设置*/
										error_information = Main_vCalibration_OtherSetting( product, mcu_control );
										break;
									case 9:
										/*特定电源的串口功能与校准功能冲突，需要进行标记操作*/
										error_information = Main_vCalibration_UsedCalibrateFlagSetting( product, mcu_control );
										break;
									default: break;
								}
								index++;
								if (error_information != string.Empty) {
									error_information += index.ToString();
								}
							}

							Thread.Sleep( 500 );
							/*关闭负载和供电电压，校准过程结束*/
							error_information_1 = Main_vCalibration_End( product, mcu_control, an97002h, it8500 );

							if (error_information != string.Empty) {
								MessageBox.Show( error_information + "		校准操作异常", "操作提示", MessageBoxButton.OK, MessageBoxImage.Information );
							}

							if (error_information_1 != string.Empty) {
								MessageBox.Show( error_information_1 + "		校准操作异常", "操作提示", MessageBoxButton.OK, MessageBoxImage.Information );
							}

							if ((error_information == string.Empty) && (error_information_1 == string.Empty)) {
								MessageBox.Show( "校准成功" );
							}
						}catch(Exception ex) {
							MessageBox.Show( ex.ToString() );
						}
					}
				}
			}

			/*校准线程执行结束，需要将控件使能状态及测试开关恢复至默认状态*/
			Dispatcher.Invoke( new dlgMain_vEnableSet( Main_vEnableSet ) , gpbGpibAddress , true );
			Dispatcher.Invoke( new dlgMain_vEnableSet( Main_vEnableSet ) , gpbLoadCurrent , true );
			Dispatcher.Invoke( new dlgMain_vEnableSet( Main_vEnableSet ) , gpbOutputChannel , true );
			power_value = false ;
			sp_acpower.Close( );
			sp_common.Close( );
			sp_product.Close( );
		}

		private string Main_vCalibration_OtherSetting( Product product , MCU_Control mcu_control )
		{
			string error_information = string.Empty;

			//是否允许备电单投功能
			if ((product.species_name >= Product.SpeciesName.IG_Z2071F) && (product.species_name <= Product.SpeciesName.IG_Z2272L)) {
				if ((product.species_name >= Product.SpeciesName.IG_Z2102L) && (product.species_name <= Product.SpeciesName.IG_Z2272L)) {
					//用户指令的设置
					error_information = mcu_control.McuControl_vSetBatCutoffVoltage( 205, product.species_name, ref sp_product ); Thread.Sleep( 800 ); //赋安要求，备电切断点设置为20.5V
					if (error_information != string.Empty) { return error_information; }
					error_information = mcu_control.McuControl_vSetBatUnderVoltage( 210, product.species_name, ref sp_product ); Thread.Sleep( 800 ); //赋安要求，备电欠压点设置为21.0V
					if (error_information != string.Empty) { return error_information; }
				}

				if ((product.species_name == Product.SpeciesName.IG_Z2071F) || (product.species_name == Product.SpeciesName.IG_Z2102F) || (product.species_name == Product.SpeciesName.IG_Z2102L)) {
					error_information = mcu_control.McuControl_vSetOverpowrSignal( product.species_name, 320m, ref sp_product ); Thread.Sleep( 200 ); //将默认过功率值设置为320W
				} else if ((product.species_name == Product.SpeciesName.IG_Z2121F) || (product.species_name == Product.SpeciesName.IG_Z2182F) || (product.species_name == Product.SpeciesName.IG_Z2182L)) {
					error_information = mcu_control.McuControl_vSetOverpowrSignal( product.species_name, 530m, ref sp_product ); Thread.Sleep( 200 ); //将默认过功率值设置为530W
				} else if ((product.species_name == Product.SpeciesName.IG_Z2181F) || (product.species_name == Product.SpeciesName.IG_Z2272F) || (product.species_name == Product.SpeciesName.IG_Z2272L)) {
					error_information = mcu_control.McuControl_vSetOverpowrSignal( product.species_name, 800m, ref sp_product ); Thread.Sleep( 200 ); //将默认过功率值设置为800W
				}

				sp_product.BaudRate = product.communicate_baudrate; Thread.Sleep( 5 );
				/*防止长时间没有响应代码造成的管理员账户等待超时退出*/
				mcu_control.McuControl_vInitialize( product.species_name, ref sp_product ); Thread.Sleep( 200 );
				sp_product.ReadExisting();

				error_information = mcu_control.McuControl_vAssign( MCU_Control.Command.Set, MCU_Control.Config.BatsSingleWorkDisable, ref sp_product, 1 ); Thread.Sleep( 100 );
			}

			return error_information;
		}

		/// <summary>
		/// 蜂鸣器工作时间的设置
		/// </summary>
		/// <param name="product"></param>
		/// <param name="mcu_control"></param>
		/// <returns></returns>
		private string Main_vCalibration_BeepWorkingTimeSetting( Product product , MCU_Control mcu_control )
		{
			string error_information = string.Empty;

			if ( !Main_bBeepWorkingTimeKeepDefault ) {
				//应急照明电源的相关设置不需要在校准模式下
				if ((product.species_name >= Product.SpeciesName.IG_Z2102L) && (product.species_name <= Product.SpeciesName.IG_Z2272L)) {
					error_information = mcu_control.McuControl_vSetBeepTime( 2, product.species_name, ref sp_product ); Thread.Sleep( 100 );
				} else if ((product.species_name >= Product.SpeciesName.IG_Z2071F) && (product.species_name <= Product.SpeciesName.IG_Z2272F)) {
					error_information = mcu_control.McuControl_vSetBeepTime( 4200, product.species_name, ref sp_product ); Thread.Sleep( 100 );
				} else if ((product.species_name == Product.SpeciesName.IG_M1202F) || (product.species_name == Product.SpeciesName.IG_M1102F) || (product.species_name == Product.SpeciesName.IG_M1302F)) {
					error_information = mcu_control.McuControl_vSetBeepTime( 4200, product.species_name, ref sp_product ); Thread.Sleep( 100 );
				} else {
					//其它电源需要在校准模式下执行（STM8S的单片机除外）
					if ((product.species_name != Product.SpeciesName.IG_M3201F__20A主机电源) && (product.species_name != Product.SpeciesName.IG_B1061F__IG_B06S01)) {
						sp_product.BaudRate = product.communicate_baudrate; Thread.Sleep( 5 );
						/*防止长时间没有响应代码造成的管理员账户等待超时退出*/
						mcu_control.McuControl_vInitialize( product.species_name, ref sp_product ); Thread.Sleep( 100 );
						sp_product.ReadExisting();

						error_information = mcu_control.McuControl_vAssign( MCU_Control.Command.Set, MCU_Control.Config.ChangeBeepWorkingTime, ref sp_product, 1 );
						Thread.Sleep( 300 );
						if (error_information != string.Empty) { return error_information; }

						mcu_control.McuControl_vAssign( MCU_Control.Command.Reset, MCU_Control.Config.DisplayVoltage_Ratio_1, ref sp_product, 1 ); //软件复位，防止对后续命令造成干扰
						Thread.Sleep( 800 );
					}
				}
			}

			return error_information;
		}

		private string Main_vCalibration_End( Product product , MCU_Control mcu_control , AN97002H an97002h , ElectronicLoad_Control_IT8500 it8500 )
		{
			string error_information = string.Empty;
			int retry_time = 0;

			sp_common.Close( );
			sp_common.BaudRate = 9600;
			Thread.Sleep( 5 );
			for ( int index_of_load = 0 ; index_of_load < UseLoad.Length ; index_of_load++ ) {
				if ( UseLoad[ index_of_load ] ) {
					retry_time = 0;
					do {
						error_information = it8500.ElectronicLoadControl_vInitializate( ref sp_common , ( byte ) ( index_of_load + 1 ) ); Thread.Sleep( 30 );
					} while ( ( ++retry_time < 5 ) && ( error_information != string.Empty ) );
					if ( error_information != string.Empty ) { return error_information; }
				}
			}

			retry_time = 0;
			do {
				error_information = an97002h.ACPower_vControlStop( 12 , ref sp_acpower ); Thread.Sleep( 50 );
			} while ( ( ++retry_time < 5 ) && ( error_information != string.Empty ) );
			if ( error_information != string.Empty ) { return error_information; }

			sp_product.Close( );
			sp_product.BaudRate = product.communicate_baudrate; Thread.Sleep( 5 );
			mcu_control.McuControl_vInitialize( product.species_name , ref sp_product ); Thread.Sleep( 100 );
			sp_product.ReadExisting( );

			/*单片机复位以生效*/
			sp_product.Close( );
			sp_product.BaudRate = product.communicate_baudrate;
			mcu_control.McuControl_vAssign( MCU_Control.Command.Reset , MCU_Control.Config.DisplayVoltage_Ratio_1 , ref sp_product , 1 );

			return error_information;
		}

		private string Main_vCalibration_MainpowerFullLoad( Product product , MCU_Control mcu_control , AN97002H an97002h , ElectronicLoad_Control_IT8500 it8500 )
		{
			string error_information = string.Empty;
			int retry_time = 0;

			decimal special_output_voltage = 0m;
			decimal special_output_current = 0m;
			ElectronicLoad_Control_IT8500.General_Data general_Data = new ElectronicLoad_Control_IT8500.General_Data( );

			/*设置主电为高*/
			retry_time = 0;
			do {
				error_information = an97002h.ACPower_vSetParameters( 12 , 2300 , 500 , 5 , 5 , 1 , true , ref sp_acpower ); ;//统一设置为较高的230V电压
				error_information = an97002h.ACPower_vControlStart( 12 , ref sp_acpower ); Thread.Sleep( 50 );
			} while ( ( ++retry_time < 5 ) && ( error_information != string.Empty ) );
			if ( error_information != string.Empty ) { return error_information; }

			/*校准的满载值写入*/
			byte[] voltage_value;
			byte[] current_value;
			decimal[] whole_current = new decimal[] { 0m , 0m , 0m };
			decimal[] voltage = new decimal[] { 0m , 0m , 0m };
			sp_common.Close( );
			sp_common.BaudRate = 9600;
			sp_product.Close( );
			sp_product.BaudRate = product.communicate_baudrate;

			/*先等待主电工作 - 判断标准：输出电压为标准电压附近    测试过主电欠压点的电源类型可以跳过本次时间等待*/
			if ( product.mainpower_undervoltage_should_calibrate == false ) {
				int index = 0;
				decimal voltage_target = 27.5m;
				if ( ( product.species_name == Product.SpeciesName.IG_B2032F ) || ( product.species_name == Product.SpeciesName.IG_B2032H ) || ( product.species_name == Product.SpeciesName.IG_B1032F ) ) { voltage_target = 13.0m; }
				while ( ++index <= 80 ) {
					for ( int index_of_load = 0 ; index_of_load < 3 ; index_of_load++ ) {
						if ( UseLoad[ index_of_load ] ) {
							retry_time = 0;
							do {
								error_information = it8500.ElectronicLoadControl_vReceivCommand( ( byte ) ( index_of_load + 1 ) , ElectronicLoad_Control_IT8500.Command_Read_General.General , ref sp_common ); Thread.Sleep( 30 );
								general_Data = it8500.ElectronicLoadControl_vGetGeneralData( );
							} while ( ( ++retry_time < 5 ) && ( error_information != string.Empty ) );
							if ( error_information != string.Empty ) { return error_information; }

							if ( general_Data.Actruly_Voltage > ( voltage_target - 0.5m ) ) {
								index = 100;  //跳出等待稳定的时间
								Thread.Sleep( 2500 ); //等待开机不响应串口指令的时间过去
							}
							break;
						}
						Thread.Sleep( 500 );
					}
				}
			}			

			//以电子负载实际对应的通道为递增顺序
			for ( int index_of_channel = 0 ; index_of_channel < 3 ; index_of_channel++ ) {
				//关闭所有输出 - 防止不同通道的电流相互干扰
				for ( int index_of_usedload = 0 ; index_of_usedload < 3 ; index_of_usedload++ ) {
					if ( UseLoad[ index_of_usedload ] ) {
						retry_time = 0;
						do {
							error_information = it8500.ElectronicLoadControl_vSendCommand( ( byte ) ( index_of_usedload + 1 ) , ElectronicLoad_Control_IT8500.Command_Set_One_Byte_Parmeter.On_Or_Off , ( byte ) ElectronicLoad_Control_IT8500.Input_Status.Off , ref sp_common ); Thread.Sleep( 30 );
							error_information = it8500.ElectronicLoadControl_vSendCommand( ( byte ) ( index_of_usedload + 1 ) , ElectronicLoad_Control_IT8500.Command_Set_One_Byte_Parmeter.Operation_Mode , ( byte ) ElectronicLoad_Control_IT8500.Operation_Mode.CC , ref sp_common ); Thread.Sleep( 30 );
						} while ( ( ++retry_time < 5 ) && ( error_information != string.Empty ) );
						if ( error_information != string.Empty ) { return error_information; }
					}
				}

				//按照通道将对应的负载带上电
				for ( int index_of_load = 0 ; index_of_load < 3 ; index_of_load++ ) {
					if ( UseLoad[ index_of_load ] ) {
						if ( LoadChannel[ index_of_load ] == index_of_channel + 1 ) {
							retry_time = 0;
							do {
								error_information = it8500.ElectronicLoadControl_vSendCommand( ( byte ) ( index_of_load + 1 ) , ElectronicLoad_Control_IT8500.Command_Set_One_Int32_Parmeter.Current_CC , Convert.ToInt32( UserSettedCurrent[ index_of_load ] * 10 ) , ref sp_common ); Thread.Sleep( 30 );
								error_information = it8500.ElectronicLoadControl_vSendCommand( ( byte ) ( index_of_load + 1 ) , ElectronicLoad_Control_IT8500.Command_Set_One_Byte_Parmeter.On_Or_Off , ( byte ) ElectronicLoad_Control_IT8500.Input_Status.On , ref sp_common ); Thread.Sleep( 1500 );
								error_information = it8500.ElectronicLoadControl_vReceivCommand( ( byte ) ( index_of_load + 1 ) , ElectronicLoad_Control_IT8500.Command_Read_General.General , ref sp_common ); Thread.Sleep( 30 );
							} while ( ( ++retry_time < 5 ) && ( error_information != string.Empty ) );
							if ( error_information != string.Empty ) { return error_information; }

							general_Data = it8500.ElectronicLoadControl_vGetGeneralData( );
							//按照电源的实际输出通道计算
							whole_current[ index_of_channel ] += general_Data.Actruly_Current;
							voltage[ index_of_channel ] = general_Data.Actruly_Voltage;
						}
					}
				}

				sp_product.BaudRate = product.communicate_baudrate; Thread.Sleep( 5 );
				/*防止长时间没有响应代码造成的管理员账户等待超时退出*/
				mcu_control.McuControl_vInitialize( product.species_name , ref sp_product ); Thread.Sleep( 100 );
				sp_product.ReadExisting( );

				if ( ( product.species_name == Product.SpeciesName.IG_B2032F ) || ( product.species_name == Product.SpeciesName.IG_B2032H ) || ( product.species_name == Product.SpeciesName.IG_B1032F ) ) {
					if ( index_of_channel == 0 ) {
						/*记录声讯12V电源的待校准的主输出通道电压*/
						special_output_voltage = voltage[ index_of_channel ];
					} else if ( index_of_channel == 1 ) {
						/*记录声讯12V电源的待校准的辅助输出通道电流*/
						special_output_current = whole_current[ index_of_channel ];
						current_value = BitConverter.GetBytes( ( Int32 ) ( special_output_current * 1000 ) );
						voltage_value = BitConverter.GetBytes( ( Int32 ) ( special_output_voltage * 1000 ) );
						error_information = mcu_control.McuControl_vAssign( MCU_Control.Command.Set , MCU_Control.Config.DisplayVoltage_Ratio_1 , ref sp_product , voltage_value[ 1 ] , voltage_value[ 0 ] , current_value[ 1 ] , current_value[ 0 ] );
						Thread.Sleep( 300 );
						break;
					}
				}

				//负载存在的情况下对电压电流进行校准
				current_value = BitConverter.GetBytes( ( Int32 ) ( whole_current[ index_of_channel ] * 1000 ) );
				voltage_value = BitConverter.GetBytes( ( Int32 ) ( voltage[ index_of_channel ] * 1000 ) );

				/*声讯12V电源需要特殊处理，其校准数据是由两路输出同时决定的  IG-B2032F  IG-B2032H  IG-B2022F(24V输出)*/
				if ( !( ( product.species_name == Product.SpeciesName.IG_B2032F ) || ( product.species_name == Product.SpeciesName.IG_B2032H ) || ( product.species_name == Product.SpeciesName.IG_B1032F ) ) ) {
					//限定只有存在实际输出的通道才可以进行带载电流的校准
					if ( index_of_channel < product.output_channel ) {
						if ( index_of_channel == 0 ) {
							if ( product.species_name == Product.SpeciesName.J_EI8212 ) { //J-EI8212电源的输出1是隔离的，测试值需要除以单片机程序中的系数 0.863 之后才能传递校准
								decimal current = whole_current[ index_of_channel ] / 0.863m * 1000;
								current_value = BitConverter.GetBytes( ( Int32 ) current );
							}
							error_information = mcu_control.McuControl_vAssign( MCU_Control.Command.Set , MCU_Control.Config.DisplayVoltage_Ratio_1 , ref sp_product , voltage_value[ 1 ] , voltage_value[ 0 ] , current_value[ 1 ] , current_value[ 0 ] );
						} else if ( index_of_channel == 1 ) {
							error_information = mcu_control.McuControl_vAssign( MCU_Control.Command.Set , MCU_Control.Config.DisplayVoltage_Ratio_2 , ref sp_product , voltage_value[ 1 ] , voltage_value[ 0 ] , current_value[ 1 ] , current_value[ 0 ] );
						} else if ( index_of_channel == 2 ) {
							error_information = mcu_control.McuControl_vAssign( MCU_Control.Command.Set , MCU_Control.Config.DisplayVoltage_Ratio_3 , ref sp_product , voltage_value[ 1 ] , voltage_value[ 0 ] , current_value[ 1 ] , current_value[ 0 ] );
						}
					}
				}
				if ( error_information != string.Empty ) { return error_information; }
				Thread.Sleep( 300 );
			}

			//关闭所有输出
			for ( int index_of_usedload = 0 ; index_of_usedload < 3 ; index_of_usedload++ ) {
				if ( UseLoad[ index_of_usedload ] ) {
					retry_time = 0;
					do {
						error_information = it8500.ElectronicLoadControl_vSendCommand( ( byte ) ( index_of_usedload + 1 ) , ElectronicLoad_Control_IT8500.Command_Set_One_Byte_Parmeter.On_Or_Off , ( byte ) ElectronicLoad_Control_IT8500.Input_Status.Off , ref sp_common ); Thread.Sleep( 30 );
					} while ( ( ++retry_time < 5 ) && ( error_information != string.Empty ) );
					if ( error_information != string.Empty ) { return error_information; }
				}
			}

			return error_information;
		}

		/*备电情况下采集的输出电流与主电电流存在比较大的偏差，且一致性不太好的电源，使用此项*/
		private string Main_vCalibration_SpCurrentRatioSetting( Product product , MCU_Control mcu_control , ElectronicLoad_Control_IT8500 it8500 , AN97002H an97002h )
		{
			string error_information = string.Empty;
			int retry_time = 0;

			//特定型号电源上实现此功能    勾选了不使用备电电流校准的情况下也需要跳过该功能的校准环节
			if ( ( !ShouldCalibrateSpCurrent ) || ( !(( ( product.species_name >= Product.SpeciesName.IG_Z2071F ) && ( product.species_name <= Product.SpeciesName.IG_Z2272L ) ) || (product.species_name == Product.SpeciesName.IG_M1202F)
				|| (product.species_name == Product.SpeciesName.IG_M1102F) || (product.species_name == Product.SpeciesName.IG_M1302F) ) )) { 
				return error_information;
			}

			//提示打开备电开关，保证备电能正常提供输出
			if ( MessageBox.Show( "请保证备电的接入（备电可以正常输出5A），请优先使用真电池进行校准操作" , "操作提示" , MessageBoxButton.YesNo , MessageBoxImage.Information , MessageBoxResult.Yes ) != MessageBoxResult.Yes ) {
				error_information = AbrotCalibration;
				return error_information;
			}

			ElectronicLoad_Control_IT8500.General_Data general_Data = new ElectronicLoad_Control_IT8500.General_Data( );
			sp_product.BaudRate = product.communicate_baudrate; Thread.Sleep( 1 );
			sp_product.ReadExisting( );

			int index = 0;
			//输出带载5A；程控关主电；使用输出负载的电流与电压参数传递给单片机
			for ( index = 0 ; index < 3 ; index++ ) {
				if ( UseLoad[ index ] ) {  //注意：此处仅使用第一通道，若是后续扩展需要注意修改此处
					retry_time = 0;
					do {
						error_information = it8500.ElectronicLoadControl_vSendCommand( ( byte ) ( index + 1 ) , ElectronicLoad_Control_IT8500.Command_Set_One_Int32_Parmeter.Current_CC , Convert.ToInt32( 10000 * 5 ) , ref sp_common ); Thread.Sleep( 30 );
						error_information = it8500.ElectronicLoadControl_vSendCommand( ( byte ) ( index + 1 ) , ElectronicLoad_Control_IT8500.Command_Set_One_Byte_Parmeter.On_Or_Off , ( byte ) ( ElectronicLoad_Control_IT8500.Input_Status.On ) , ref sp_common ); Thread.Sleep( 30 );
					} while ( ( ++retry_time < 5 ) && ( error_information != string.Empty ) );
				}
			}

			retry_time = 0;
			do {
				error_information = an97002h.ACPower_vControlStop( 12 , ref sp_acpower ); Thread.Sleep( 50 );
			} while ( ( ++retry_time < 5 ) && ( error_information != string.Empty ) );
			if ( error_information != string.Empty ) { return error_information; }
			Thread.Sleep( 1500 );

			/*先等待备电工作 - 判断标准：输出电压为标准电压附近*/
			index = 0;
			decimal voltage_target = 23.5m;
			while ( ++index <= 80 ) {
				for ( int index_of_load = 0 ; index_of_load < 3 ; index_of_load++ ) {
					if ( UseLoad[ index_of_load ] ) {
						retry_time = 0;
						do {
							error_information = it8500.ElectronicLoadControl_vReceivCommand( ( byte ) ( index_of_load + 1 ) , ElectronicLoad_Control_IT8500.Command_Read_General.General , ref sp_common ); Thread.Sleep( 30 );
							general_Data = it8500.ElectronicLoadControl_vGetGeneralData( );
						} while ( ( ++retry_time < 5 ) && ( error_information != string.Empty ) );
						if ( error_information != string.Empty ) { return error_information; }

						if ( general_Data.Actruly_Voltage > ( voltage_target - 1.0m ) ) {
							index = 100;  //跳出等待稳定的时间	
							Thread.Sleep( 1500 );
						}
						break;
					}
					Thread.Sleep( 500 );
				}
			}

			if ( index < 100 ) {
				MessageBox.Show( "待校准电源在规定时间内输出电压未能建立，请注意此异常" , "异常操作提示" );
				error_information = Calibration_Error;
				return error_information;
			}			

			//MCU金手指指令
			sp_product.Close( );
			sp_product.BaudRate = product.communicate_baudrate;
			mcu_control.McuControl_vInitialize( product.species_name , ref sp_product ); Thread.Sleep( 10 );
			sp_product.ReadExisting( );

			for ( index = 0 ; index < 3 ; index++ ) {
				if ( UseLoad[ index ] ) {  //注意：此处仅使用第一通道，若是后续扩展需要注意修改此处
					retry_time = 0;
					do {
						error_information = it8500.ElectronicLoadControl_vReceivCommand( ( byte ) ( index + 1 ) , ElectronicLoad_Control_IT8500.Command_Read_General.General , ref sp_common ); Thread.Sleep( 30 );
					} while ( ( ++retry_time < 5 ) && ( error_information != string.Empty ) );

					general_Data = it8500.ElectronicLoadControl_vGetGeneralData( );
					byte[] voltage = BitConverter.GetBytes( Convert.ToUInt16( general_Data.Actruly_Voltage * 1000 ) );
					byte[] current = BitConverter.GetBytes( Convert.ToUInt16( general_Data.Actruly_Current * 1000 ) );

					error_information = mcu_control.McuControl_vAssign( MCU_Control.Command.Set , MCU_Control.Config.RatioSpCurrentToMp_1, ref sp_product , voltage[ 1 ] , voltage[ 0 ] , current[ 1 ] , current[ 0 ] ); Thread.Sleep( 100 );
				}
			}

			//解除负载情况
			for ( index = 0 ; index < 3 ; index++ ) {
				if ( UseLoad[ index ] ) {  //注意：此处仅使用第一通道，若是后续扩展需要注意修改此处
					retry_time = 0;
					do {
							error_information = it8500.ElectronicLoadControl_vSendCommand( ( byte ) ( index + 1 ) , ElectronicLoad_Control_IT8500.Command_Set_One_Byte_Parmeter.On_Or_Off , ( byte ) ( ElectronicLoad_Control_IT8500.Input_Status.Off ) , ref sp_common ); Thread.Sleep( 30 );
					} while ( ( ++retry_time < 5 ) && ( error_information != string.Empty ) );
				}
			}

			/*单片机复位以生效 - 包含单片机重启后一段时间不响应串口代码的时间*/
			error_information = mcu_control.McuControl_vAssign( MCU_Control.Command.Reset , MCU_Control.Config.DisplayVoltage_Ratio_1 , ref sp_product , 1 );
			Thread.Sleep( 2000 );

			return error_information;
		}

		private string Main_vCalibration_OCPSetting( Product product , MCU_Control mcu_control )
		{
			string error_information = string.Empty;
			/*写入OCP - 应急照明电源不需要执行此操作*/
			if ( !( ( product.species_name >= Product.SpeciesName.IG_Z2071F ) && ( product.species_name <= Product.SpeciesName.IG_Z2272L ) ) ) {

				sp_product.BaudRate = product.communicate_baudrate; Thread.Sleep( 5 );
				/*防止长时间没有响应代码造成的管理员账户等待超时退出*/
				mcu_control.McuControl_vInitialize( product.species_name , ref sp_product ); Thread.Sleep( 100 );
				sp_product.ReadExisting( );

				decimal[] ocp_value = new decimal[] { product.desinged_ocp_1 , product.desinged_ocp_2 , product.desinged_ocp_3 };
				sp_product.Close( );
				sp_product.BaudRate = product.communicate_baudrate;
				byte[] value_u16;

				if ( !( ( product.species_name == Product.SpeciesName.IG_B2032F ) || ( product.species_name == Product.SpeciesName.IG_B2032H ) || ( product.species_name == Product.SpeciesName.IG_B1032F ) ) ) {
					for ( int index_of_channel = 0 ; index_of_channel < 3 ; index_of_channel++ ) {
						value_u16 = BitConverter.GetBytes( Convert.ToUInt16( ocp_value[ index_of_channel ] ) );
						//限定只有存在实际输出的通道才可以进行带载电流的校准
						if ( index_of_channel < product.output_channel ) {
							if ( index_of_channel == 0 ) {
								error_information = mcu_control.McuControl_vAssign( MCU_Control.Command.Set , MCU_Control.Config.OverCurrentSet_1 , ref sp_product , value_u16[ 1 ] , value_u16[ 0 ] );
							} else if ( index_of_channel == 1 ) {
								error_information = mcu_control.McuControl_vAssign( MCU_Control.Command.Set , MCU_Control.Config.OverCurrentSet_2 , ref sp_product , value_u16[ 1 ] , value_u16[ 0 ] );
							} else if ( index_of_channel == 2 ) {
								error_information = mcu_control.McuControl_vAssign( MCU_Control.Command.Set , MCU_Control.Config.OverCurrentSet_3 , ref sp_product , value_u16[ 1 ] , value_u16[ 0 ] );
							}
							Thread.Sleep( 300 );
						}
					}
				} else {
					value_u16 = BitConverter.GetBytes( Convert.ToUInt16( ocp_value[ 1 ] ) );
					error_information = mcu_control.McuControl_vAssign( MCU_Control.Command.Set , MCU_Control.Config.OverCurrentSet_1 , ref sp_product , value_u16[ 1 ] , value_u16[ 0 ] );
					Thread.Sleep( 300 );
				}
				if ( error_information != string.Empty ) { return error_information; }
			}

			/*单片机复位以生效 - 包含单片机重启后一段时间不响应串口代码的时间*/
			error_information = mcu_control.McuControl_vAssign( MCU_Control.Command.Reset , MCU_Control.Config.DisplayVoltage_Ratio_1 , ref sp_product , 1 );
			Thread.Sleep( 1500 );

			return error_information;
		}

		private string Main_vCalibration_UsedCalibrateFlagSetting( Product product , MCU_Control mcu_control )
		{
			string error_information = string.Empty;

			if ( CanUseUartAfterCalibration != false ) {
				return error_information;
			}

			Thread.Sleep( 500 );
			if ((product.species_name == Product.SpeciesName.IG_B1051H) || ( product.species_name == Product.SpeciesName.IG_B2053F ) || ( product.species_name == Product.SpeciesName.IG_B2073F ) || ( product.species_name == Product.SpeciesName.IG_B2053H ) 
				|| (product.species_name == Product.SpeciesName.IG_B2053K)	|| ( product.species_name == Product.SpeciesName.IG_X1032F ) ){
				sp_product.BaudRate = product.communicate_baudrate; Thread.Sleep( 5 );
				mcu_control.McuControl_vInitialize( product.species_name , ref sp_product );	Thread.Sleep( 200 );
				sp_product.ReadExisting( );

				//写入串口使用之后的标记
				error_information = mcu_control.McuControl_vAssign(   MCU_Control.Command.Set, MCU_Control.Config.SetBeValidatedFlag, ref sp_product,1 ); Thread.Sleep( 100 );
			}

			return error_information;
		}		

		private string Main_vCalibration_MainpowerUnderVoltage( Product product , MCU_Control mcu_control , AN97002H an97002h , ElectronicLoad_Control_IT8500 it8500 )
		{
			string error_information = string.Empty;
			int retry_time = 0;
			ElectronicLoad_Control_IT8500.General_Data general_Data = new ElectronicLoad_Control_IT8500.General_Data( );

			/*限定主电欠压点校准所需种类 - 只有采用方波判断主电状态的电源才需要增加*/
			if ( product.mainpower_undervoltage_should_calibrate == false ) {
				return error_information;
			}

			/*提示关备电*/
			if ( MessageBox.Show( "请保证备电关闭状态" , "操作提示" , MessageBoxButton.YesNo , MessageBoxImage.Information , MessageBoxResult.Yes ) != MessageBoxResult.Yes ) {
				error_information = AbrotCalibration;
				return error_information;
			}

			Thread.Sleep( 1000 ); //等待1s进行虚电的放电

			/*由于某些电源在主电电压较低时启动时间长，可能对主电欠压点存在影响，此处先使用较高的主电电压投置可以正常启动状态一段时间，然后再调整主电电压置170V以设置主电欠压恢复点*/
			retry_time = 0;
			do {
				error_information = an97002h.ACPower_vSetParameters( 12 , 2400 , 500 , 5 , 5 , 1 , true , ref sp_acpower );Thread.Sleep( 50 );
				error_information = an97002h.ACPower_vControlStart( 12 , ref sp_acpower ); Thread.Sleep( 50 );
			} while ( ( ++retry_time < 5 ) && ( error_information != string.Empty ) );
			if ( error_information != string.Empty ) { return error_information; }	

			/*先等待主电工作 - 判断标准：输出电压为标准电压附近*/
			int index = 0;
			decimal voltage_target = 27.5m;
			if ( ( product.species_name == Product.SpeciesName.IG_B2032F ) || ( product.species_name == Product.SpeciesName.IG_B2032H ) || ( product.species_name == Product.SpeciesName.IG_B1032F ) ) { voltage_target = 13.0m; } else if ( product.species_name == Product.SpeciesName.J_EI8212 ) { voltage_target = 25.5m; } //输出1为稳压输出  标准值为25.5V
			while ( ++index <= 80 ) {
				for ( int index_of_load = 0 ; index_of_load < 3 ; index_of_load++ ) {
					if ( UseLoad[ index_of_load ] ) {
						retry_time = 0;
						do {
							error_information = it8500.ElectronicLoadControl_vReceivCommand( ( byte ) ( index_of_load + 1 ) , ElectronicLoad_Control_IT8500.Command_Read_General.General , ref sp_common ); Thread.Sleep( 30 );
							general_Data = it8500.ElectronicLoadControl_vGetGeneralData( );
						} while ( ( ++retry_time < 5 ) && ( error_information != string.Empty ) );
						if ( error_information != string.Empty ) { return error_information; }

						if ( general_Data.Actruly_Voltage > ( voltage_target - 1.0m ) ) {
							index = 100;  //跳出等待稳定的时间

							/*此处将主电调节到170V统一设置主电欠压点  - 海湾20A主机电源除外，其欠压点应该设置为165V的情况*/
							retry_time = 0;
							do {
								error_information = an97002h.ACPower_vSetParameters( 12 , 1700 , 500 , 5 , 5 , 1 , true , ref sp_acpower );Thread.Sleep( 50 );
							} while ( ( ++retry_time < 5 ) && ( error_information != string.Empty ) );
							if ( error_information != string.Empty ) { return error_information; }
							Thread.Sleep( 4500 ); //等待开机不响应串口指令的时间过去和主电电压参数值获取成功

						}
						break;
					}
					Thread.Sleep( 500 );
				}
			}

			if ( index < 100 ) {
				MessageBox.Show( "待校准电源在规定时间内输出电压未能建立，请注意此异常" , "异常操作提示" );
				error_information = Calibration_Error;
				return error_information;
			}

			/*金手指动作*/
			sp_product.Close( );
			sp_product.BaudRate = product.communicate_baudrate;
			Thread.Sleep( 5 );
			mcu_control.McuControl_vInitialize( product.species_name , ref sp_product );
			Thread.Sleep( 200 );
			sp_product.ReadExisting( );

			/*主电周期获取*/
			error_information = mcu_control.McuControl_vAssign( MCU_Control.Command.Set , MCU_Control.Config.Mcu_MainpowerPeriodCountGet , ref sp_product );
			if ( error_information != string.Empty ) { return error_information; }
			Thread.Sleep( 300 );

			/*主电欠压点获取 - 关键参数，指令连续发送两次，防止Flash操作异常*/
			for ( index = 0 ; index < 2 ; index++ ) {
				error_information = mcu_control.McuControl_vAssign( MCU_Control.Command.Set , MCU_Control.Config.Mcu_MainpowerUnderVoltageCountGet , ref sp_product );
				if ( error_information != string.Empty ) { return error_information; }
				Thread.Sleep( 300 );
			}

			/*对应急照明电源而言，需要在220V时进行主电电压的校准*/
			if (product.mainpower_voltage_should_calirate != false) {

				byte[] real_voltage;
				do {
					error_information = an97002h.ACPower_vSetParameters( 12, 2200, 500, 5, 5, 1, true, ref sp_acpower ); Thread.Sleep( 50 );
				} while ((++retry_time < 5) && (error_information != string.Empty));
				Thread.Sleep( 1000 );

				if ((product.species_name == Product.SpeciesName.IG_Z2071F) || (product.species_name == Product.SpeciesName.IG_Z2102F) || (product.species_name == Product.SpeciesName.IG_Z2102L)) {
					real_voltage = BitConverter.GetBytes( 215 ); //实际测试值偏高，此处使用较低的值进行校准
				} else {
					real_voltage = BitConverter.GetBytes( 220 );
				}
				error_information = mcu_control.McuControl_vAssign( MCU_Control.Command.Set, MCU_Control.Config.MainpowerVoltageCalibrate, ref sp_product, real_voltage[ 0 ], real_voltage[ 1 ] );
				if (error_information != string.Empty) { return error_information; }
				Thread.Sleep( 500 );
			}

			/*主电高端停止充点电获取 -- 只有特定产品上实现*/
			if ( product.species_name == Product.SpeciesName.J_EI8212 ) {
				retry_time = 0;
				do {
					error_information = an97002h.ACPower_vSetParameters( 12 , 2770 , 500 , 5 , 5 , 1 , true , ref sp_acpower ); Thread.Sleep( 50 );
				} while ( ( ++retry_time < 5 ) && ( error_information != string.Empty ) );
				if ( error_information != string.Empty ) { return error_information; }
				Thread.Sleep( 3000 ); //等待交流电压输出的稳定
				error_information = mcu_control.McuControl_vAssign( MCU_Control.Command.Set , MCU_Control.Config.Mcu_CannotChargeHighCountGet , ref sp_product );
				if ( error_information != string.Empty ) { return error_information; }
				Thread.Sleep( 300 );
			}

			return error_information;
		}

		private string Main_vCalibration_CurrentZero( Product product , MCU_Control mcu_control , ElectronicLoad_Control_IT8500 it8500 )
		{
			string error_information = string.Empty;
			int index = 0;
			int retry_time = 0;
			ElectronicLoad_Control_IT8500.General_Data general_Data = new ElectronicLoad_Control_IT8500.General_Data( );

			decimal voltage_target = 21.3m;
			if ( ( product.species_name == Product.SpeciesName.IG_B2032F ) || ( product.species_name == Product.SpeciesName.IG_B2032H ) || ( product.species_name == Product.SpeciesName.IG_B1032F ) ) { voltage_target = 11.5m; } else if ( ( product.species_name == Product.SpeciesName.IG_Z2071F ) || ( product.species_name == Product.SpeciesName.IG_Z2121F ) || ( product.species_name == Product.SpeciesName.IG_Z2181F ) ) {
				voltage_target = 31.0m;
			}
			while ( ++index <= 80 ) {
				for ( int index_of_load = 0 ; index_of_load < 3 ; index_of_load++ ) {
					if ( UseLoad[ index_of_load ] ) {
						do {
							error_information = it8500.ElectronicLoadControl_vReceivCommand( ( byte ) ( index_of_load + 1 ) , ElectronicLoad_Control_IT8500.Command_Read_General.General , ref sp_common ); Thread.Sleep( 30 );
							general_Data = it8500.ElectronicLoadControl_vGetGeneralData( );
						} while ( ( ++retry_time < 5 ) && ( error_information != string.Empty ) );
						if ( error_information != string.Empty ) { return error_information; }

						if ( general_Data.Actruly_Voltage > ( voltage_target - 0.5m ) ) {
							index = 100;  //跳出等待稳定的时间
							Thread.Sleep( 2000 ); //等待开机不响应串口指令的时间过去
						}
						break;
					}
					Thread.Sleep( 100 );
				}
			}

			if ( index < 100 ) {
				//在规定的时间内输出没有建立，此种情况下不允许进行后续的测试
				error_information = "备电重启后在规定的时间内没有输出建立，请重新校准";
				return error_information;
			}

			/*执行到此处，需要保证之前的通道对应的负载都为空载，防止零点电流校准偏移*/
			for ( index = 0 ; index < product.output_channel ; index++ ) {
				byte[] voltage = new byte[] { 0 , 0 };
				if ( UseLoad[ index ] ) {
					error_information = it8500.ElectronicLoadControl_vSendCommand( ( byte ) ( index + 1 ) , ElectronicLoad_Control_IT8500.Command_Set_One_Byte_Parmeter.On_Or_Off , ( byte ) ( ElectronicLoad_Control_IT8500.Input_Status.Off ) , ref sp_common );
					if ( error_information != string.Empty ) { return error_information; }
					Thread.Sleep( 200 );
				}
			}

			/*金手指动作*/
			sp_product.Close( );
			sp_product.BaudRate = product.communicate_baudrate;
			mcu_control.McuControl_vInitialize( product.species_name , ref sp_product );
			Thread.Sleep( 300 );
			sp_product.ReadExisting( );

			for ( index = 0 ; index < product.output_channel ; index++ ) {
				byte[] voltage = new byte[] { 0 , 0 };
				if ( UseLoad[ index ] ) {
					error_information = it8500.ElectronicLoadControl_vReceivCommand( ( byte ) ( index + 1 ) , ElectronicLoad_Control_IT8500.Command_Read_General.General , ref sp_common );
					if ( error_information != string.Empty ) { return error_information; }
					general_Data = it8500.ElectronicLoadControl_vGetGeneralData( );
					voltage = BitConverter.GetBytes( ( Int32 ) ( general_Data.Actruly_Voltage * 1000 ) );

					switch ( index ) {
						case 0:/*零点校准1*/
							error_information = mcu_control.McuControl_vAssign( MCU_Control.Command.Set , MCU_Control.Config.ZeroCalibrate_1 , ref sp_product , voltage[ 1 ] , voltage[ 0 ] );
							break;
						case 1:/*零点校准2*/
							error_information = mcu_control.McuControl_vAssign( MCU_Control.Command.Set , MCU_Control.Config.ZeroCalibrate_2 , ref sp_product , voltage[ 1 ] , voltage[ 0 ] );
							break;
						case 2:/*零点校准3*/
							error_information = mcu_control.McuControl_vAssign( MCU_Control.Command.Set , MCU_Control.Config.ZeroCalibrate_3 , ref sp_product , voltage[ 1 ] , voltage[ 0 ] );
							break;
						default:
							break;
					}
				}
				if ( error_information != string.Empty ) { return error_information; }
				Thread.Sleep( 200 );
			}

			/*输出带载用于耗电 - 使用CR模式*/
			for ( int index_of_load = 0 ; index_of_load < UseLoad.Length ; index_of_load++ ) {
				if ( UseLoad[ index_of_load ] ) {
					retry_time = 0;
					do {
						error_information = it8500.ElectronicLoadControl_vSendCommand( ( byte ) ( index_of_load + 1 ) , ElectronicLoad_Control_IT8500.Command_Set_One_Byte_Parmeter.Operation_Mode , ( byte ) ElectronicLoad_Control_IT8500.Operation_Mode.CR , ref sp_common ); Thread.Sleep( 30 );
						error_information = it8500.ElectronicLoadControl_vSendCommand( ( byte ) ( index_of_load + 1 ) , ElectronicLoad_Control_IT8500.Command_Set_One_Int32_Parmeter.Restance_CR , CR_VALUE , ref sp_common ); Thread.Sleep( 30 );      //设置为50Ω进行输出虚电的放电操作
						error_information = it8500.ElectronicLoadControl_vSendCommand( ( byte ) ( index_of_load + 1 ) , ElectronicLoad_Control_IT8500.Command_Set_One_Byte_Parmeter.On_Or_Off , ( byte ) ElectronicLoad_Control_IT8500.Input_Status.On , ref sp_common ); Thread.Sleep( 30 );
					} while ( ( ++retry_time < 5 ) && ( error_information != string.Empty ) );
					if ( error_information != string.Empty ) { return error_information; }
				}
			}

			return error_information;
		}

		private string Main_vCalibration_StandbyPower( Product product , MCU_Control mcu_control , ElectronicLoad_Control_IT8500 it8500 ,AN97002H an97002h )
		{
			string error_information = string.Empty;
			int retry_time = 0;
			ElectronicLoad_Control_IT8500.General_Data general_Data = new ElectronicLoad_Control_IT8500.General_Data( );

			//备电确定启动之后才可以继续后续的校准操作
			int index = 0;
			decimal voltage_target = 21.3m;
			if ( ( product.species_name == Product.SpeciesName.IG_B2032F ) || ( product.species_name == Product.SpeciesName.IG_B2032H ) || ( product.species_name == Product.SpeciesName.IG_B1032F ) ) { voltage_target = 11.5m; } else if ( ( product.species_name == Product.SpeciesName.IG_Z2071F ) || ( product.species_name == Product.SpeciesName.IG_Z2121F ) || ( product.species_name == Product.SpeciesName.IG_Z2181F ) ) {
				voltage_target = 31.0m;
			}
			while ( ++index <= 80 ) {
				for ( int index_of_load = 0 ; index_of_load < 3 ; index_of_load++ ) {
					if ( UseLoad[ index_of_load ] ) {
						retry_time = 0;
						do {
							error_information = it8500.ElectronicLoadControl_vReceivCommand( ( byte ) ( index_of_load + 1 ) , ElectronicLoad_Control_IT8500.Command_Read_General.General , ref sp_common ); Thread.Sleep( 30 );
							general_Data = it8500.ElectronicLoadControl_vGetGeneralData( );
						} while ( ( ++retry_time < 5 ) && ( error_information != string.Empty ) );
						if ( error_information != string.Empty ) { return error_information; }

						if ( general_Data.Actruly_Voltage > ( voltage_target - 0.5m ) ) {
							index = 100;  //跳出等待稳定的时间
							Thread.Sleep( 2000 ); //等待开机不响应串口指令的时间过去
						}
						break;
					}
					Thread.Sleep( 100 );
				}
			}

			if ( index < 100 ) {
				//在规定的时间内输出没有建立，此种情况下不允许进行后续的测试
				error_information = "备电重启后在规定的时间内没有输出建立，请重新校准";
				return error_information;
			}

			/*金手指动作*/
			sp_product.Close( );
			sp_product.BaudRate = product.communicate_baudrate;
			mcu_control.McuControl_vInitialize( product.species_name , ref sp_product );
			Thread.Sleep( 150 );
			sp_product.ReadExisting( );

			//备电无法单投的产品需要在此处关闭主电
			if ( ( product.species_name == Product.SpeciesName.IG_Z2102L ) || ( product.species_name == Product.SpeciesName.IG_Z2182L ) || ( product.species_name == Product.SpeciesName.IG_Z2272L ) ) { 
				retry_time = 0;
				do {
					error_information = an97002h.ACPower_vControlStop( 12 , ref sp_acpower ); Thread.Sleep( 50 );
				} while ( ( ++retry_time < 5 ) && ( error_information != string.Empty ) );
				if ( error_information != string.Empty ) { return error_information; }
				Thread.Sleep( 2000 ); //等待电源切换到备电上进行工作
			}

			/*备电电压校准 - 将通道1空载时的电压作为备电电压 - 注意：J-EI8212 需要使用第二路输出电压来校准*/
			for ( int index_of_load = 0 ; index_of_load < UseLoad.Length ; index_of_load++ ) {
				if ( product.species_name == Product.SpeciesName.J_EI8212 ) {
					if(index_of_load != 1 ) {
						continue;
					}
				}
				if ( UseLoad[ index_of_load ] ) {
					retry_time = 0;
					do {
						error_information = it8500.ElectronicLoadControl_vReceivCommand( ( byte ) ( index_of_load + 1 ) , ElectronicLoad_Control_IT8500.Command_Read_General.General , ref sp_common ); Thread.Sleep( 30 );
						general_Data = it8500.ElectronicLoadControl_vGetGeneralData( );
					} while ( ( ++retry_time < 5 ) && ( error_information != string.Empty ) );
					if ( error_information != string.Empty ) { return error_information; }

					byte[] voltage_code = BitConverter.GetBytes( Convert.ToInt32( 1000 * general_Data.Actruly_Voltage ) );
					error_information = mcu_control.McuControl_vAssign( MCU_Control.Command.Set , MCU_Control.Config.DisplayVoltage_Ratio_Bat , ref sp_product , voltage_code[ 1 ] , voltage_code[ 0 ] );
					Thread.Sleep( 300 );
					if ( error_information != string.Empty ) { return error_information; }
					break;
				}
			}

			//备电无法单投的产品需要在此处打开主电
			if ( ( product.species_name == Product.SpeciesName.IG_Z2102L ) || ( product.species_name == Product.SpeciesName.IG_Z2182L ) || ( product.species_name == Product.SpeciesName.IG_Z2272L ) ) {
				retry_time = 0;
				do {
					error_information = an97002h.ACPower_vControlStart( 12 , ref sp_acpower ); Thread.Sleep( 500 );
				} while ( ( ++retry_time < 5 ) && ( error_information != string.Empty ) );
				if ( error_information != string.Empty ) { return error_information; }
			}

			/*单片机复位以生效 - 包含单片机重启后一段时间不响应串口代码的时间*/
			error_information = mcu_control.McuControl_vAssign( MCU_Control.Command.Reset , MCU_Control.Config.DisplayVoltage_Ratio_1 , ref sp_product , 1 );
			Thread.Sleep( 500 );

			return error_information;
		}

		private string Main_vCalibration_Initalize( Product product , MCU_Control mcu_control , AN97002H an97002h , ElectronicLoad_Control_IT8500 it8500 )
		{
			string error_information = string.Empty;
			int retry_time = 0;

			sp_common.Close( );
			sp_common.BaudRate = 9600;
			Thread.Sleep( 5 );
			for ( int index_of_load = 0 ; index_of_load < UseLoad.Length ; index_of_load++ ) {
				if ( UseLoad[ index_of_load ] ) {
					retry_time = 0;
					do {
						error_information = it8500.ElectronicLoadControl_vInitializate( ref sp_common , ( byte ) ( index_of_load + 1 ) ); Thread.Sleep( 10 );
					} while ( ( ++retry_time < 5 ) && ( error_information != string.Empty ) );
				}
			}
			if ( error_information != string.Empty ) { return error_information; }

			if ( !( ( product.species_name == Product.SpeciesName.IG_Z2102L ) || ( product.species_name == Product.SpeciesName.IG_Z2182L ) || ( product.species_name == Product.SpeciesName.IG_Z2272L ) ) ) {
				retry_time = 0;
				do {
					error_information = an97002h.ACPower_vControlStop( 12 , ref sp_acpower ); Thread.Sleep( 50 );
				} while ( ( ++retry_time < 5 ) && ( error_information != string.Empty ) );
				if ( error_information != string.Empty ) { return error_information; }
			}

			/*金手指动作*/
			sp_product.Close( );
			sp_product.BaudRate = product.communicate_baudrate;
			Thread.Sleep( 5 );
			mcu_control.McuControl_vInitialize( product.species_name , ref sp_product );
			Thread.Sleep( 100 );
			sp_product.ReadExisting( );

			/*单片机校准之前需要将校准数据擦除 - STM8S芯片的单片机没有此功能*/
			if ( ( product.species_name != Product.SpeciesName.IG_B1061F__IG_B06S01 ) && ( product.species_name != Product.SpeciesName.IG_M3201F__20A主机电源 ) ) {
				error_information = mcu_control.McuControl_vAssign( MCU_Control.Command.Set , MCU_Control.Config.Mcu_ClearValidationCode , ref sp_product ); Thread.Sleep( 100 );
				if ( error_information != string.Empty ) { return error_information; }
			}

			/*输出带载用于耗电 - 使用CR模式*/
			for ( int index_of_load = 0 ; index_of_load < UseLoad.Length ; index_of_load++ ) {
				if ( UseLoad[ index_of_load ] ) {
					retry_time = 0;
					do {
						error_information = it8500.ElectronicLoadControl_vSendCommand( ( byte ) ( index_of_load + 1 ) , ElectronicLoad_Control_IT8500.Command_Set_One_Byte_Parmeter.Operation_Mode , ( byte ) ElectronicLoad_Control_IT8500.Operation_Mode.CR , ref sp_common ); Thread.Sleep( 30 );
						error_information = it8500.ElectronicLoadControl_vSendCommand( ( byte ) ( index_of_load + 1 ) , ElectronicLoad_Control_IT8500.Command_Set_One_Int32_Parmeter.Restance_CR , CR_VALUE , ref sp_common ); Thread.Sleep( 30 );      //设置为50Ω进行输出虚电的放电操作
						error_information = it8500.ElectronicLoadControl_vSendCommand( ( byte ) ( index_of_load + 1 ) , ElectronicLoad_Control_IT8500.Command_Set_One_Byte_Parmeter.On_Or_Off , ( byte ) ElectronicLoad_Control_IT8500.Input_Status.On , ref sp_common ); Thread.Sleep( 30 );
					} while ( ( ++retry_time < 5 ) && ( error_information != string.Empty ) );
					if ( error_information != string.Empty ) { return error_information; }
				}
			}

			/*单片机复位以生效 - 包含单片机重启后一段时间不响应串口代码的时间*/
			error_information = mcu_control.McuControl_vAssign( MCU_Control.Command.Reset , MCU_Control.Config.DisplayVoltage_Ratio_1 , ref sp_product , 1 );
			Thread.Sleep( 500 );

			return error_information;
		}


		private void CobSerialPort_Common_SelectionChanged( object sender , SelectionChangedEventArgs e )
		{
			if ( cobSerialPort_Common.SelectedIndex >= 0 ) {
				string uart_name = cobSerialPort_Common.SelectedItem.ToString( );
				string name = string.Empty;
				int index = uart_name.LastIndexOf( " " );
				name = uart_name.Substring( index + 1 );
				sp_common = new SerialPort( name , 9600 , Parity.None , 8 , StopBits.One );
			}
		}

		private void PowerButton1_Click( object sender , RoutedEventArgs e )
		{
			string error_information = string.Empty;
			try {
				if ( power_value == true ) {
					using ( AN97002H acpower = new AN97002H( ) ) {
						error_information = acpower.ACPower_vControlStart( 12 , ref sp_acpower );
						if ( error_information != string.Empty ) { MessageBox.Show( "开始工作指令错误，请重试" ); power_value = false; }
					}
				} else {
					using ( AN97002H acpower = new AN97002H( ) ) {
						error_information = acpower.ACPower_vControlStop( 12 , ref sp_acpower );
						if ( error_information != string.Empty ) { MessageBox.Show( "停止工作指令错误，请重试" ); power_value = true; }
					}
				}
			} catch {
				MessageBox.Show( "请检查程控交流电源使用的串口是否正常" , "故障报警" );
			}
		}



		private void Switch1_Click( object sender , RoutedEventArgs e )
		{
			if ( switch_value != false ) {
				/*紧急退出交流电压的步进操作*/
				if ( trdACPowerWorking != null ) { if ( trdACPowerWorking.IsAlive ) { ctsExitVoltageStepChange.Cancel( ); } else { switch_value = false; } } else { switch_value = false; }
			} else {
				/*无效操作*/
			}
		}

		/// <summary>
		/// 将明确的电源产品型号类型填写到下拉菜单中
		/// </summary>
		/// <param name="sender"></param>
		/// <param name="e"></param>
		private void CobSpeciesProduct_PreviewMouseDown( object sender , MouseButtonEventArgs e )
		{
			ComboBox cob = sender as ComboBox;
			cob.Items.Clear( );
			Product product = new Product( );
			for ( int index = 0 ; index < product.Species.Length ; index++ ) {
				cobSpeciesProduct.Items.Add( product.Species[ index ] );
			}

			//禁止产品使用串口的选择
			cobSerialPort_Product.IsEnabled = false;
			sp_product = null;
		}

		/// <summary>
		/// 限定所有的文本框只能输入0~9；超过此限制则无法输入
		/// </summary>
		/// <param name="sender"></param>
		/// <param name="e"></param>
		private void TxtPreviewKeyDown( object sender , KeyEventArgs e )
		{
			if ( ( e.Key >= Key.D0 && e.Key <= Key.D9 ) || ( e.Key >= Key.NumPad0 && e.Key <= Key.NumPad9 ) ||
   e.Key == Key.Delete || e.Key == Key.Back || e.Key == Key.Left || e.Key == Key.Right || e.Key == Key.OemPeriod || e.Key == Key.Decimal ) {
				//按下了Alt、ctrl、shift等修饰键
				if ( e.KeyboardDevice.Modifiers != ModifierKeys.None ) {
					e.Handled = true;
				}
			} else//按下了字符等其它功能键
			{
				e.Handled = true;
			}
		}

		/// <summary>
		/// 使用到程控电源的串口的重新定义
		/// </summary>
		/// <param name="sender"></param>
		/// <param name="e"></param>
		private void CobSerialPort_ACpower_SelectionChanged( object sender , SelectionChangedEventArgs e )
		{
			ComboBox cob = sender as ComboBox;
			int select_index = cob.SelectedIndex;
			if ( sp_acpower == null ) {
				if ( select_index >= 0 ) {
					string select_com_name = cob.SelectedValue.ToString( );
					sp_acpower = new SerialPort( select_com_name , 9600 , Parity.None , 8 , StopBits.One );
				}
			} else {
				if ( select_index >= 0 ) {
					if ( sp_acpower.IsOpen ) { sp_acpower.Close( ); }
					string select_com_name = cob.SelectedValue.ToString( );
					sp_acpower = new SerialPort( select_com_name , 9600 , Parity.None , 8 , StopBits.One );
				}
			}
		}

		/// <summary>
		/// 使用到的电源的串口的重新定义
		/// </summary>
		/// <param name="sender"></param>
		/// <param name="e"></param>
		private void CobSerialPort_Product_SelectionChanged( object sender , SelectionChangedEventArgs e )
		{
			ComboBox cob = sender as ComboBox;
			int select_index = cob.SelectedIndex;
			if ( select_index >= 0 ) {
				string select_com_name = cob.SelectedValue.ToString( );
				sp_product = new SerialPort( select_com_name , 9600 , product.serial_parity , 8 , StopBits.One );
			}
		}

		private void ChkOutput_Checked( object sender , RoutedEventArgs e )
		{
			CheckBox chk = sender as CheckBox;
			int index = Convert.ToInt32( chk.Name.Substring( chk.Name.Length - 1 ) ) - 1;

			grdLoadCurrent.Children[ index ].Visibility = Visibility.Visible;
			if ( index == 0 ) { grdLoad1Channel.Visibility = Visibility.Visible; lblLoad1.Visibility = Visibility.Visible; }
			if ( index == 1 ) { grdLoad2Channel.Visibility = Visibility.Visible; lblLoad2.Visibility = Visibility.Visible; }
			if ( index == 2 ) { grdLoad3Channel.Visibility = Visibility.Visible; lblLoad3.Visibility = Visibility.Visible; }

			grdLoad1Channel.Children[ index ].Visibility = Visibility.Visible;
			grdLoad2Channel.Children[ index ].Visibility = Visibility.Visible;
			grdLoad3Channel.Children[ index ].Visibility = Visibility.Visible;

			UseLoad[ index ] = true;
		}

		private void ChkOutput_Unchecked( object sender , RoutedEventArgs e )
		{
			CheckBox chk = sender as CheckBox;
			int index = Convert.ToInt32( chk.Name.Substring( chk.Name.Length - 1 ) ) - 1;

			grdLoadCurrent.Children[ index ].Visibility = Visibility.Hidden;
			if ( index == 0 ) { grdLoad1Channel.Visibility = Visibility.Hidden; lblLoad1.Visibility = Visibility.Hidden; }
			if ( index == 1 ) { grdLoad2Channel.Visibility = Visibility.Hidden; lblLoad2.Visibility = Visibility.Hidden; }
			if ( index == 2 ) { grdLoad3Channel.Visibility = Visibility.Hidden; lblLoad3.Visibility = Visibility.Hidden; }

			grdLoad1Channel.Children[ index ].Visibility = Visibility.Hidden;
			grdLoad2Channel.Children[ index ].Visibility = Visibility.Hidden;
			grdLoad3Channel.Children[ index ].Visibility = Visibility.Hidden;

			UseLoad[ index ] = false;
		}

		private void ChkBeep_30S_Checked( object sender , RoutedEventArgs e )
		{
			Main_bBeepWorkingTimeKeepDefault = true;
		}

		private void ChkBeep_30S_UnChecked( object sender , RoutedEventArgs e )
		{
			Main_bBeepWorkingTimeKeepDefault = false;
		}

		private void BtnMainpowerUnderVoltage_Click( object sender , RoutedEventArgs e )
		{
			/*将主电输出调整到165V*/
			if ( sp_acpower != null ) {
				string error_information = string.Empty;
				using ( AN97002H acpower = new AN97002H( ) ) {
					sp_acpower.BaudRate = 9600;
					error_information = acpower.ACPower_vSetParameters( 12 , 1650 , 500 , 5 , 5 , 1 , false , ref sp_acpower );
					if ( error_information != string.Empty ) { MessageBox.Show( "设置状态错误，请重试" ); return; }
					error_information = acpower.ACPower_vControlStart( 12 , ref sp_acpower );
					if ( error_information != string.Empty ) { MessageBox.Show( "开始工作指令错误，请重试" ); }
				}
			} else {
				MessageBox.Show( "请选择使用串口之后重试" );
			}
		}

		private void BtnMainpowerUnderVoltageRecovery_Click( object sender , RoutedEventArgs e )
		{
			/*将主电输出调整到185V*/
			if ( sp_acpower != null ) {
				string error_information = string.Empty;
				using ( AN97002H acpower = new AN97002H( ) ) {
					sp_acpower.BaudRate = 9600;
					error_information = acpower.ACPower_vSetParameters( 12 , 1850 , 500 , 5 , 5 , 1 , false , ref sp_acpower );
					if ( error_information != string.Empty ) { MessageBox.Show( "设置状态错误，请重试" ); return; }
					error_information = acpower.ACPower_vControlStart( 12 , ref sp_acpower );
					if ( error_information != string.Empty ) { MessageBox.Show( "开始工作指令错误，请重试" ); }
				}
			} else {
				MessageBox.Show( "请选择使用串口之后重试" );
			}
		}

		private void BtnMainpowerOverVoltage_Click( object sender , RoutedEventArgs e )
		{
			/*将主电输出调整到295V*/
			if ( sp_acpower != null ) {
				string error_information = string.Empty;
				using ( AN97002H acpower = new AN97002H( ) ) {
					sp_acpower.BaudRate = 9600;
					error_information = acpower.ACPower_vSetParameters( 12 , 2950 , 500 , 5 , 5 , 1 , false , ref sp_acpower );
					if ( error_information != string.Empty ) { MessageBox.Show( "设置状态错误，请重试" ); return; }
					error_information = acpower.ACPower_vControlStart( 12 , ref sp_acpower );
					if ( error_information != string.Empty ) { MessageBox.Show( "开始工作指令错误，请重试" ); }
				}
			} else {
				MessageBox.Show( "请选择使用串口之后重试" );
			}
		}

		private void BtnMainpowerOverVoltageRecovery_Click( object sender , RoutedEventArgs e )
		{
			/*将主电输出调整到265V*/
			if ( sp_acpower != null ) {
				string error_information = string.Empty;
				using ( AN97002H acpower = new AN97002H( ) ) {
					sp_acpower.BaudRate = 9600;
					error_information = acpower.ACPower_vSetParameters( 12 , 2650 , 500 , 5 , 5 , 1 , false , ref sp_acpower );
					if ( error_information != string.Empty ) { MessageBox.Show( "设置状态错误，请重试" ); return; }
					error_information = acpower.ACPower_vControlStart( 12 , ref sp_acpower );
					if ( error_information != string.Empty ) { MessageBox.Show( "开始工作指令错误，请重试" ); }
				}
			} else {
				MessageBox.Show( "请选择使用串口之后重试" );
			}
		}

		/// <summary>
		/// 校准之后是否依然能使用串口功能
		/// </summary>
		/// <param name="sender"></param>
		/// <param name="e"></param>
		private void CheckBox_Checked( object sender , RoutedEventArgs e )
		{
			CanUseUartAfterCalibration = true;
		}

		private void CheckBox_Unchecked( object sender , RoutedEventArgs e )
		{
			CanUseUartAfterCalibration = false;
		}


		private void ChkSpCurrentValidate_Checked(object sender,RoutedEventArgs e)
		{
			ShouldCalibrateSpCurrent = false;
		}

		private void ChkSpCurrentValidate_UnChecked( object sender , RoutedEventArgs e )
		{
			ShouldCalibrateSpCurrent = true;
		}
	}
}
