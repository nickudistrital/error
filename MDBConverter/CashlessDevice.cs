using System;
using System.Text;
using System.Threading;
using Abrantix.MDB2Serial.Common;
using System.IO;
using SOLVEN.Common;
using SOLVEN;
using EmailSender;
using System.Threading.Tasks;
using POS;
using System.Collections.Generic;
using System.Diagnostics;

namespace Abrantix.MDB2Serial.MDBConverter
{
    /// <summary>
    /// This is a MDB cashless device sample implementation for a device processing e.g. credit cards.
    /// A transaction can be triggered by requesting the processing of the <see cref="SimulationCommand.SwipeCard"/> with <see cref="ProcessCommand"/>.
    /// <remarks>
    /// This is a very simple implementation an must probably be adjusted for real use.
    /// E.g. there is no handling for overlapping command handling:
    /// When receiving a VEND request from the VMC, this sample implementation will answer with VEND approved
    /// (or denied, depending on <see cref="ProvokeVendDenied"/>). Any VEND cancel sent by the VMC in between are not taken care of.
    /// For a real implementation, this must be considered.
    /// </remarks>
    /// </summary>
    public class CashlessDevice : IDisposable
    {
        // Parameters

        #region Initial Parameters

        /// <summary>
        /// Handles the whole serial protocol (CP).
        /// </summary>
        private MDBStreamDriver serialDriver;


        /// <summary>
        /// Maximum number of retries for a single message.
        /// </summary>
        public const int MDB_MAX_RETRIES = 20;

        /// <summary>
        /// Trace Flags for the process of the vending machine.
        /// </summary>
        private MDBTraceFlag traceFlags;

        /// <summary>
        /// Get or Set the level of detail for log messages.
        /// </summary>
        public MDBTraceFlag TraceFlags
        {
            get { return this.traceFlags; }
            set
            {
                // Always trace this
                OnMessageTrace(string.Format("CashlessDeviceSimulator: Setting TraceFlags to {0}", value));
                this.traceFlags = value;
            }
        }

        /// <summary>
        /// Consume this event if you want to listen to trace messages of this class.
        /// </summary>
        public event MessageTraceEventHandler MessageTrace;

        /// <summary>
        /// These are the states of the cashless device. See MDB Spec section 7 for details.
        /// The states that are provided here are a very simple version of an MDB cashless device state machine
        /// and work nicely with this sample implementation.
        /// It is very probable that you will have to adjust these states for your needs.
        /// </summary>
        public enum ReaderState
        {
            Inactive,

            /// <summary>
            /// This is not an official MDB state,
            /// but it is handy to treat it as one for this sample
            /// </summary>
            MdbResetReceived,

            /// <summary>
            /// This is not an official MDB state,
            /// but it is handy to treat it as one for this sample
            /// </summary>
            Reset,

            Disabled,
            Enabled,
            SessionIdle,
            Vend,
        }

        /// <summary>
        /// Reader Status in all the flow in the vending machine
        /// </summary>
        private ReaderState readerState;

        /// <summary>
        /// Vending machine status before buy the process and after this process
        /// </summary>
        public enum VendSessionState
        {
            Idle,
            CardSwiped,
            SessionBegun,
            VendRequested,
            VendApproved,
            VendCommited,
            VendDenied,
            AwaitVendResult,
            WaitForNextMVCCommand,
        }

        /// <summary>
        /// Vending Session status machine for the specific or current session
        /// </summary>
        private VendSessionState vendSessionState;

        /// <summary>
        /// Manual Reset Event
        /// </summary>
        private ManualResetEvent cardSwipeSimulationCommandReceived;

        /// <summary>
        /// Set this if you want to simulate denied authorizations.
        /// </summary>
        public bool ProvokeVendDenied { get; set; }

        /// <summary>
        /// Instruction to be processed by the cashless device simulator.
        /// </summary>
        public enum SimulationCommand
        {
            /// <summary>
            /// Swipe a card.
            /// </summary>
            SwipeCard,
        }

        /// <summary>
        /// Constructor Method
        /// </summary>
        /// <param name="mdbStream"></param>
        /// <param name="deviceAddress"></param>
        public CashlessDevice(Stream mdbStream, byte deviceAddress)
        {
            InitConfiguration(mdbStream, deviceAddress);
        }

        #endregion

        #region Base Core Parameters

        /// <summary>
        /// Set this to terminate the cashless device.
        /// This typically will only be done on shutdown of the whole system.
        /// </summary>
        public bool ShutdownExpected { get; set; }

        /// <summary>
        /// Set amount of the specific item
        /// </summary>
        public byte[] RequestedAmount { get; set; }

        /// <summary>
        /// Current Transaction Timer for the transaction
        /// </summary>
        private Stopwatch _currentTransactionTimer;

        /// <summary>
        /// Time out of the transaction (1 min + 20 delay)
        /// </summary>
        public const long TRANSACTION_TIMEOUT = 80000;

        /// <summary>
        /// Pos Services
        /// </summary>
        private PosService _posService;

        /// <summary>
        /// Delegate used for the credit authorization.
        /// </summary>
        private delegate void authorization(string amountString);

        /// <summary>
        /// Simple delegate used for all asynchronous operations in this sample.
        /// Maybe this might be adjusted to take parameters, 
        /// or several different delegates might be used for the real implementation.
        /// </summary>
        private delegate void operation();

        /// <summary>
        /// Start/Stop payment process
        /// </summary>
        private authorization beginAuthorize { get; set; }

        /// <summary>
        /// All requests that simply need an ACK (no data) will set this flag.
        /// The response will be sent in the end of this routine in this case.
        /// </summary>
        private bool SendMDBAck { get; set; }

        #endregion

        /// <summary>
        /// Serial Number of the Order
        /// </summary>
        private string _serialNumber;

        /// <summary>
        /// Flag process
        /// </summary>
        private bool _process { get; set; }
        private bool _init_real_process { get; set; }

        /// <summary>
        /// Tries of the main process in payment
        /// </summary>
        private int TRIES { get; set; }

        /// <summary>
        /// Maximum number of retries for a pos message.
        /// </summary>
        public const int POS_MAX_RETRIES = 3;

        #region Cancel Parameters

        private Queue<Action> _voidQueue;
        private string _currentInvoiceNumber;

        #endregion

        #region Settings

        /// <summary>
        /// Init the process of the Vending Machine Adapter
        /// </summary>
        /// <param name="mdbStream"></param>
        /// <param name="deviceAddress"></param>
        private void InitConfiguration(Stream mdbStream, byte deviceAddress)
        {
            InitSerialDriver(mdbStream);
            ResetTraceFlags();
            CashlessDeviceOff();
            SetSlaveMDB();
            SetDeviceAddress(deviceAddress);
            SetFirstPollProcess();
            CardSwipeResetEvent();
        }

        /// <summary>
        /// Initializacion Serial Driver MDB (Vending machine adapter).
        /// </summary>
        private void InitSerialDriver(Stream mdbStream)
        {
            this.serialDriver = new MDBStreamDriver(mdbStream, MDB_MAX_RETRIES, true);
            this.serialDriver.MessageTrace += serialDriver_MessageTrace;
            this.serialDriver.DetailMessageTrace += serialDriver_DetailMessageTrace;
        }

        /// <summary>
        /// Sets the default trace flags.
        /// </summary>
        public void ResetTraceFlags()
        {
            this.TraceFlags = MDBTraceFlag.HighLevel | MDBTraceFlag.MDB | MDBTraceFlag.StateMachine;
        }

        /// <summary>
        /// Starup is OFF the cashless devices 
        /// </summary>
        private void CashlessDeviceOff()
        {
            // On startup, the cashless device is 'off'
            this.readerState = ReaderState.Inactive;
            this.vendSessionState = VendSessionState.Idle;
        }

        /// <summary>
        /// Set the slave mode for the vending machine
        /// </summary>
        private void SetSlaveMDB()
        {
            //set MDB Mode to Slave
            try
            {
                serialDriver.SendMessageAwaitCPAck(CPControlCode.MDB_MODE, new byte[] {0x00}, true);
            }
            catch
            {
            }
        }

        /// <summary>
        /// Set device address in the vending
        /// </summary>
        private void SetDeviceAddress(byte deviceAddress)
        {
            //set Device Address
            try
            {
                serialDriver.SendMessageAwaitCPAck(CPControlCode.ADDRESSREGISTER, new byte[] {deviceAddress}, true);
            }
            catch
            {
            }
        }

        /// <summary>
        /// First status of the Vending Machine (JUST_RESET)
        /// </summary>
        private void SetFirstPollProcess()
        {
            // The first poll of the master will be answered with JUST_RESET
            this.serialDriver.EnqueueMDBCommand(CPControlCode.DATA, new byte[] {(byte) MDBReaderCommand.JUST_RESET});
        }

        /// <summary>
        /// Init Status of the CardSwipe Status
        /// </summary>
        private void CardSwipeResetEvent()
        {
            this.cardSwipeSimulationCommandReceived = new ManualResetEvent(false);
        }

        /// <summary>
        /// Shutdown the vending machine
        /// </summary>
        public void Dispose()
        {
            this.serialDriver.Shutdown();
        }

        #endregion

        #region Base Core

        /// <summary>
        /// This is the main MDB  loop that runs until <see cref="ShutdownExpected"/>.
        /// </summary>
        public void Run()
        {
            // Reset Amount
            InitVariablesMachine();
            // Main Runner
            while (!this.ShutdownExpected)
            {
                CatchProcess();
            }
        }

        private void InitVariablesMachine()
        {
            // Reset Amount
            RequestedAmount = new byte[2];
            // Flag process
            _process = false;
            _init_real_process = false;
            // Tries process
            TRIES = 0;
            // Payment Process
            beginAuthorize = new authorization(this.BeginAuthorize);
        }

        /// <summary>
        /// Try and Catch All the diferent process in the cicle of the web
        /// </summary>
        private void CatchProcess()
        {
            try
            {
                StartProcess();
            }
            catch (TimeoutException)
            {
                Trace("No data read from Master. Read again...", MDBTraceFlag.HighLevel);
            }
            catch (MDBResetException)
            {
                Trace("Received MDB Bus Reset. Michael: ", MDBTraceFlag.HighLevel);
                SetReaderState(ReaderState.MdbResetReceived);
            }
            catch (Exception ex)
            {
                Trace("Error generado controlado. Michael: ", MDBTraceFlag.HighLevel);
                Trace(ex.ToString(), MDBTraceFlag.HighLevel);
            }
        }

        /// <summary>
        /// Start CORE Process of all functions
        /// </summary>
        private void StartProcess()
        {
            // Read VMC message from underlying port
            byte[] vmcData = this.serialDriver.ReadMessage();

            if ((vmcData != null) && (vmcData.Length > 0))
            {
                // Reset the process
                SetMDBBack(false);
                // Check actions or status of the process
                MDBMasterProcess(vmcData);
                // Verify if the process is reset
                SendMessageAwaitCPA();
            }
        }

        private void SendMessageAwaitCPA()
        {
            // If is sendmdback set the data null
            if (this.SendMDBAck)
            {
                SetReaderConfig(null);
            }
        }

        /// <summary>
        /// Send MDB Back if is possible
        /// </summary>
        /// <param name="sendMDBAck"></param>
        private void SetMDBBack(bool sendMDBAck)
        {
            this.SendMDBAck = sendMDBAck;
        }

        #endregion

        /// <summary>
        /// MDB Master Process, all the status of the Vending machine adapter (CORE)
        /// </summary>
        /// <param name="vmcData"></param>
        private void MDBMasterProcess(byte[] vmcData)
        {
            MDBMasterCommand masterCommand = (MDBMasterCommand) vmcData[0];
            // Trace(string.Format("+ LOGS # 1:  Master Command: {0}", masterCommand), MDBTraceFlag.StateMachine);
            // Trace(string.Format("+ LOGS # 2:  Reader State: {0}", this.readerState), MDBTraceFlag.StateMachine);
            // Trace(string.Format("+ LOGS # 3:  Vend State: {0}", this.vendSessionState), MDBTraceFlag.StateMachine);

            switch (masterCommand)
            {
                case MDBMasterCommand.RESET:
                    MDBMasterReset(masterCommand);
                    break;
                case MDBMasterCommand.SETUP:
                    MDBMasterSetup(masterCommand, vmcData);
                    break;
                case MDBMasterCommand.EXPANSION:
                    MDBMasterExpansion(masterCommand, vmcData);
                    break;
                case MDBMasterCommand.REVALUE:
                    MDBMasterRevalue(masterCommand, vmcData);
                    break;
                case MDBMasterCommand.READER:
                    MDBMasterReader(masterCommand, vmcData);
                    break;
                case MDBMasterCommand.POLL:
                    MDBMasterPoll(masterCommand, vmcData);
                    break;
                case MDBMasterCommand.VEND:
                    MDBMasterVend(masterCommand, vmcData);
                    break;
                default:
                    Trace(string.Format("MDBMasterProcess Unknown command: '{0}'", vmcData.ToHexString()),
                        MDBTraceFlag.MDB);
                    break;
            }
        }

        #region MDB RESET

        /// <summary>
        /// Initial Process and status of the vending machine adapter
        /// </summary>
        /// <param name="masterCommand"></param>
        private void MDBMasterReset(MDBMasterCommand masterCommand)
        {
            Trace(string.Format("MDBMasterReset --> {0}", masterCommand), MDBTraceFlag.StateMachine);
            MDBReset();
        }

        /// <summary>
        /// MDB Reset all
        /// </summary>
        private void MDBReset()
        {
            // Commit vend session here if vending state is Approved at this point in time.
            // See MDB Spec. V4, Section 7.4.7
            SetVendSessionState(VendSessionState.Idle);

            // Clear Messages
            this.serialDriver.ClearSlaveMessageQueue();

            // Show Message
            Trace(string.Format("MDBReset <-- {0}", MDBReaderCommand.JUST_RESET), MDBTraceFlag.StateMachine);

            // Reset The machine
            byte[] justReset = new byte[] {(byte) MDBReaderCommand.JUST_RESET};
            SetReaderConfig(justReset);

            // Return to inactive mode
            SetReaderState(ReaderState.Reset);
        }

        #endregion

        #region MDB SETUP

        private void MDBMasterSetup(MDBMasterCommand masterCommand, byte[] vmcData)
        {
            Trace(string.Format("MDBMasterSetup --> {0} 0x{1:X}", masterCommand, vmcData[1]),
                MDBTraceFlag.StateMachine);

            switch (vmcData[1])
            {
                // CONFIG DATA
                case 0x00:
                    MDBSetupConfigData(vmcData);
                    break;
                // MAX / MIN PRICES
                case 0x01:
                    // Reset the process - No Data
                    SetMDBBack(true);
                    break;
                default:
                    Trace(string.Format("MDBMasterSetup Unknown command: '{0}'", vmcData.ToHexString()),
                        MDBTraceFlag.MDB);
                    break;
            }
        }

        /// <summary>
        /// MDB Setup - Config Data (0x00)
        /// </summary>
        /// <param name="vmcData"></param>
        private void MDBSetupConfigData(byte[] vmcData)
        {
            // Config Message
            SetupDisplayMessage(vmcData);

            // Workaround: Some Machines do not send a RESET, but start RESET sequence with CONFIG DATA
            SetVendSessionState(VendSessionState.Idle);

            // Send Message
            Trace(string.Format("MDBSetupConfigData <-- {0}", MDBReaderCommand.CONFIG_DATA), MDBTraceFlag.StateMachine);

            // Set Data in VM
            MDBSCSetConfig();
        }

        /// <summary>
        /// Setup - Config - Display Message
        /// </summary>
        /// <param name="vmcData"></param>
        private void SetupDisplayMessage(byte[] vmcData)
        {
            int featureLevel = -1;
            if (vmcData.Length > 3)
            {
                featureLevel = (int) vmcData[2];
            }

            int columnsOnDisplay = -1;
            if (vmcData.Length > 4)
            {
                columnsOnDisplay = (int) vmcData[3];
            }

            int rowsOnDisplay = -1;
            if (vmcData.Length > 5)
            {
                rowsOnDisplay = (int) vmcData[4];
            }

            int displayInfo = -1;
            if (vmcData.Length >= 6)
            {
                displayInfo = (int) vmcData[5];
            }

            // Display Message
            string message = string.Format(
                "Received VMC Config Data. Feature Level: '{0}'. Columns On Display: '{1}'. Rows On Display: '{2}'. Display Info: '{3}'.",
                featureLevel, columnsOnDisplay, rowsOnDisplay, displayInfo);
            Trace(message, MDBTraceFlag.HighLevel);
        }

        /// <summary>
        /// Set Real Config to the Vending Machine (MDB Setting Config (0x00))
        /// </summary>
        private void MDBSCSetConfig()
        {
            /* More real config data
             * Z2:
             * 0x02: Reader Level 2
             * Z3-Z4
             * 0x19 / 0x79: ISO currency code 978 (EUR)
             * Z5
             * 0x01: Scale factor for prices
             * Z6
             * 0x02: Number of decimal places for prices (here 2: 20.00 USD)
             * Z7
             * 0x0A: Timeout for commands. Here 20 seconds.
             * Z8
             * 0x00: None of the miscellaneous options are set. 
             */
            //byte[] data = new byte[] { (byte)MDBReaderCommand.CONFIG_DATA, 0x02, 0x19, 0x78, 0x01, 0x02, 0x14, 0x00 };
            byte[] data = new byte[] {(byte) MDBReaderCommand.CONFIG_DATA, 0x01, 0x00, 0x01, 0x01, 0x02, 0x0A, 0x00};
            SetReaderConfig(data);
        }

        #endregion

        #region MDB EXPANSION

        private void MDBMasterExpansion(MDBMasterCommand masterCommand, byte[] vmcData)
        {
            Trace(string.Format("MDBMasterExpansion --> {0} 0x{1:X}", masterCommand, vmcData[1]),
                MDBTraceFlag.StateMachine);

            switch (vmcData[1])
            {
                // REQUEST ID
                case 0x00:
                    // Set Config
                    MDBExpansionRequest(vmcData);
                    // Set Data in VM
                    MDBERSetConfig();
                    break;
                default:
                    Trace(string.Format("MDBMasterExpansion Unknown command: '{0}'", vmcData.ToHexString()),
                        MDBTraceFlag.MDB);
                    break;
            }
        }

        /// <summary>
        /// Genereate the config needly for the Expansion; method Request ID
        /// </summary>
        /// <param name="vmcData"></param>
        private void MDBExpansionRequest(byte[] vmcData)
        {
            string manufacturerCode = "n/a";
            if (vmcData.Length > 3)
            {
                manufacturerCode = Encoding.ASCII.GetString(vmcData, 2, 3);
            }

            _serialNumber = "n/a";
            if (vmcData.Length > 16)
            {
                _serialNumber = Encoding.ASCII.GetString(vmcData, 5, 12);
            }

            string modelNumber = "n/a";
            if (vmcData.Length > 28)
            {
                modelNumber = Encoding.ASCII.GetString(vmcData, 17, 12);
            }

            string softwareVersion = "n/a";
            if (vmcData.Length >= 30)
            {
                softwareVersion = string.Format("{0}{1}", vmcData[29], vmcData[30]);
            }

            // Display Message
            string message = string.Format(
                "Received VMC Expansion Data. Manufacturer Code: '{0}'. Serial Number: '{1}'. Model Number: '{2}'. Software Version: '{3}'.",
                manufacturerCode, _serialNumber, modelNumber, softwareVersion);
            Trace(message, MDBTraceFlag.HighLevel);
            Trace(string.Format("MDBExpansionConfigData <-- {0}", MDBReaderCommand.PERIPHERAL_ID),
                MDBTraceFlag.StateMachine);
        }

        /// <summary>
        /// Send the data Expansion - Request with all the config
        /// </summary>
        private void MDBERSetConfig()
        {
            byte[] data = new byte[]
            {
                (byte) MDBReaderCommand.PERIPHERAL_ID, 0x41, 0x42, 0x58, 0x20, 0x20, 0x20, 0x20,
                0x20, 0x20, 0x20, 0x20, 0x20, 0x20, 0x20, 0x20, 0x41, 0x33, 0x20, 0x20, 0x20,
                0x20, 0x20, 0x20, 0x20, 0x20, 0x20, 0x20, 0x15, 0x31
            };
            SetReaderConfig(data);
        }

        #endregion

        #region MDB REVALUE

        private void MDBMasterRevalue(MDBMasterCommand masterCommand, byte[] vmcData)
        {
            Trace(string.Format("MDBMasterRevalue --> {0} 0x{1:X}", masterCommand, vmcData[1]),
                MDBTraceFlag.StateMachine);

            switch (vmcData[1])
            {
                // REVALUE LIMIT REQUEST
                case 0x01:
                    Trace(string.Format("MDBMasterRevalue <-- {0}", MDBReaderCommand.REVALUE_LIMIT_AMOUNT),
                        MDBTraceFlag.StateMachine);
                    // Set Config
                    MDBRRSetConfig();
                    break;

                default:
                    Trace(string.Format("MDBMasterRevalue Unknown command: '{0}'", vmcData.ToHexString()),
                        MDBTraceFlag.MDB);
                    break;
            }
        }

        /// <summary>
        /// Set Revalue - Revalue limit Request - Set Config in vending machine
        /// </summary>
        private void MDBRRSetConfig()
        {
            byte[] data = new byte[] {(byte) MDBReaderCommand.REVALUE_LIMIT_AMOUNT, 0x00, 0x00};
            SetReaderConfig(data);
        }

        #endregion

        #region MDB READER

        private void MDBMasterReader(MDBMasterCommand masterCommand, byte[] vmcData)
        {
            Trace(string.Format("MDBMasterReader --> {0} 0x{1:X}", masterCommand, vmcData[1]),
                MDBTraceFlag.StateMachine);

            switch (vmcData[1])
            {
                case 0x00:
                    // Set Reader in Disable
                    SetReaderState(ReaderState.Disabled);
                    // Reset the process - No Data
                    SetMDBBack(true);
                    break;
                case 0x01:
                    // Reader has been enabled
                    SetReaderState(ReaderState.Enabled);
                    // Active Machine
                    CheckDataResetMachine();
                    // Reset the process - No Data
                    SetMDBBack(true);
                    break;
                case 0x02:
                    // Reader: Option CANCEL
                    CancelVend();
                    break;
                default:
                    Trace(string.Format("MDBMasterReader Unknown command: '{0}'", vmcData.ToHexString()),
                        MDBTraceFlag.MDB);
                    // Reset the process - No Data
                    SetMDBBack(true);
                    break;
            }
        }

        #endregion

        #region MDB POLL

        private void MDBMasterPoll(MDBMasterCommand masterCommand, byte[] vmcData)
        {
            if (vmcData.Length < 2)
            {
                // Trace(string.Format("MDBMasterPoll-1 Unknown command: '{0}'", vmcData.ToHexString()),
                  //  MDBTraceFlag.MDB);
            }
            else
            {
                Trace(string.Format("MDBMasterPoll --> {0} 0x{1:X}", masterCommand, vmcData[1]),
                    MDBTraceFlag.StateMachine);
            }

            switch (this.readerState)
            {
                case ReaderState.SessionIdle:
                    // Active Machine
                    _process = false;
                    CheckDataResetMachine();
                    break;
                case ReaderState.Vend:
                    MDBPVend(vmcData);
                    break;
                case ReaderState.MdbResetReceived:
                    // Reset MDB
                    MDBReset();
                    break;
                default:
                    Trace(string.Format("MDBMasterPoll-2 Unknown command: '{0}'", vmcData.ToHexString()),
                        MDBTraceFlag.MDB);
                    // Reset the process - No Data
                    SetMDBBack(true);
                    break;
            }
        }

        private void MDBPVend(byte[] vmcData)
        {
            // Respond according to vend session state
            switch (this.vendSessionState)
            {
                case VendSessionState.CardSwiped:
                    MDBPCard();
                    break;
                // case VendSessionState.SessionBegun:
                // Todo Timer if is needle
                // break;
                case VendSessionState.VendRequested:
                    MDBPSessionRequest();
                    break;
                case VendSessionState.VendApproved:
                    MDBPVendApproved();
                    break;
                case VendSessionState.VendDenied:
                    MDBPVendDenied();
                    break;
                case VendSessionState.AwaitVendResult:
                    //This state should not occur but prevents ACK'ing a potential POLL command sent from VMC while waiting for VEND Success/Failure/Cancel
                    SetMDBBack(false);
                    break;
                default:
                    // Trace(string.Format("MDBPVend Unknown command: '{0}'", vmcData.ToHexString()), MDBTraceFlag.MDB);
                    // Reset the process - No Data
                    SetMDBBack(true);
                    break;
            }
        }

        private void MDBPCard()
        {
            Trace(string.Format("MDBPCard <-- {0}", MDBReaderCommand.BEGIN_SESSION), MDBTraceFlag.StateMachine);

            // Example: grant 5.- as virtual credit
            byte[] availableFunds = new byte[2];
            int amountLimit = 0500;

            availableFunds[0] = (byte) (amountLimit >> 8);
            availableFunds[1] = (byte) (amountLimit >> 0);

            byte[] beginSession = new byte[]
            {
                (byte) MDBReaderCommand.BEGIN_SESSION, availableFunds[0],
                availableFunds[1], 0xFF, 0xFF, 0xFF, 0xFF, 0x00, 0x00, 0x00
            };
            SetReaderConfig(beginSession);

            SetVendSessionState(VendSessionState.SessionBegun);
        }

        /// <summary>
        /// Approve payment with same amount as requested by VMC
        /// </summary>
        private void MDBPVendApproved()
        {
            Trace(string.Format("MDBPVendApproved <-- {0}", MDBReaderCommand.VEND_APPROVED), MDBTraceFlag.StateMachine);

            byte[] approved = new byte[]
            {
                (byte) MDBReaderCommand.VEND_APPROVED, RequestedAmount[0], RequestedAmount[1]
            };
            SetReaderConfig(approved);

            SetVendSessionState(VendSessionState.WaitForNextMVCCommand);
        }

        private void MDBPVendDenied()
        {
            Trace(string.Format("MDBPVendDenied <-- {0}", MDBReaderCommand.VEND_DENIED), MDBTraceFlag.StateMachine);

            byte[] deneid = new byte[] {(byte) MDBReaderCommand.VEND_DENIED};
            SetReaderConfig(deneid);

            CancelAllMachine();

            SetVendSessionState(VendSessionState.WaitForNextMVCCommand);

            // No Data response
            SetMDBBack(true);
        }

        private void MDBPSessionRequest()
        {
            //user has swiped the card but has not selected a product yet
            if (_process)
            {
                TimeSpan elapsedTime = _currentTransactionTimer.Elapsed;
                Trace(
                    string.Format("Total processing time before selecting a product… {0:00}:{1:00}.{2}",
                        elapsedTime.Minutes, elapsedTime.Seconds, elapsedTime.Milliseconds), MDBTraceFlag.HighLevel);

                if (elapsedTime.TotalMilliseconds > TRANSACTION_TIMEOUT)
                {
                    CancelVend();
                }

                // Reset the process - No Data
                SetMDBBack(true);
            }
        }

        #endregion

        #region MDB VEND

        private void MDBMasterVend(MDBMasterCommand masterCommand, byte[] vmcData)
        {
            Trace(string.Format("MDBMasterVend --> {0} 0x{1:X}", masterCommand, vmcData[1]), MDBTraceFlag.StateMachine);

            // this is the actual transaction state machine
            switch (vmcData[1])
            {
                // VEND request
                case 0x00:
                    MDBVRequest(vmcData);
                    break;
                // VEND Cancel
                case 0x01:
                    MDBVCancel();
                    break;
                // VEND Success
                case 0x02:
                    MDBVSuccess();
                    break;
                // VEND Failure
                case 0x03:
                    MDBVFailure();
                    break;
                // VEND session complete
                case 0x04:
                    MDBVSessionComplete();
                    break;
                default:
                    Trace(string.Format("MDBMasterVend Unknown command: '{0}'", vmcData.ToHexString()),
                        MDBTraceFlag.MDB);
                    break;
            }
        }

        private void MDBVRequest(byte[] vmcData)
        {
            if (this.vendSessionState == VendSessionState.SessionBegun)
            {
                //store requested amount
                RequestedAmount = new byte[2];
                RequestedAmount[0] = vmcData[2];
                RequestedAmount[1] = vmcData[3];

                string amount = RequestedAmount.ToStringAmount();

                if (amount != null)
                {

                    // Change State for keep data
                    SetVendSessionState(VendSessionState.VendRequested);

                    // amount = "001"; // Only TEST
                    Trace("CheckSateProcess", string.Format("AMOUNT: {0}", amount), MDBTraceFlag.HighLevel);
                    Trace("CheckSateProcess", string.Format("_process: {0}", _process), MDBTraceFlag.HighLevel);
                    Trace("CheckSateProcess", string.Format("_init_real_process: {0}", _init_real_process), MDBTraceFlag.HighLevel);

                    if (_init_real_process == false)
                    {
                        // Start Process
                        _init_real_process = true;

                        TRIES = 0;

                        ResetTime();

                        // Authorization is non blocking
                        beginAuthorize.BeginInvoke(amount, null, null);
                    }
                    
                    Trace("CheckSateProcess", string.Format("_process: {0}", _process), MDBTraceFlag.HighLevel);
                }
                else
                {
                    //could not read amount
                    Trace("Could not read the requested amount. Cancelling request.", MDBTraceFlag.MDB);
                    SetVendSessionState(VendSessionState.VendDenied);
                }
            }
            // Response will be sent as answer of a following POLL
        }

        /// <summary>
        /// MDB calls for a two phase commit protocol for the payment.
        /// In this routine, the card will be authorized.
        /// The transaction will be commited in <see cref="CommitAuthorization"/>.
        /// </summary>
        private void BeginAuthorize(string amount)
        {
            var success = _posService.RequestSale(amount, ref _currentInvoiceNumber);

            if (success)
            {
                Logger.Log("Invoice Number: " + _currentInvoiceNumber);
                SetVendSessionState(VendSessionState.VendApproved);
            }
            else
            {
                if (TRIES == (POS_MAX_RETRIES + 1))
                {
                    //if there is a void action queued, it will be executed now
                    _voidQueue.Dequeue();

                    TRIES = 0;

                    Logger.Log("Sale Failed");

                    // SendTransactionErrorEmail(string.Format("A sale transaction has failed @ {0}", Logger.GetDateTimeString()));

                    SetVendSessionState(VendSessionState.VendDenied);
                }
                else
                {

                    TRIES += 1;
                    BeginAuthorize(amount);
                }
            }

            if (TRIES == (POS_MAX_RETRIES + 1))
            {
                //if there is a void action queued, it will be executed now
                _voidQueue.Dequeue();

                Trace(string.Format("Authorization result: {0}", this.vendSessionState), MDBTraceFlag.HighLevel);
            }
        }

        /// <summary>
        /// Sends an email via SMTP notifying that a POS transaction has failed.
        /// </summary>
        private void SendTransactionErrorEmail(string message)
        {
            var emailService = new EmailService();
            bool success = emailService.SendEmail(
                SOLVEN.Common.Constants.MAIL_TO,
                "SOLVEN - Transaction Error",
                string.Format("Serial Number: {0}\n\n{1}", _serialNumber, message));

            if (success) Logger.Log("Sent email notification");
            else Logger.Log("Email notification failed");
        }

        private void MDBVCancel()
        {
            // Response will be sent as answer of a following POLL
            CancelVend();
        }

        private void MDBVSuccess()
        {
            if (!(this.vendSessionState == VendSessionState.AwaitVendResult))
            {
                SetVendSessionState(VendSessionState.AwaitVendResult);

                // This is a blocking operation.
                CommitAuthorization();
                // No Data response
                SetMDBBack(true);
            }
        }

        /// <summary>
        /// Commit ongoing authorization here.
        /// </summary>
        private void CommitAuthorization()
        {
            // Commit transaction here
            Trace("Transaction commited.", MDBTraceFlag.HighLevel);
            SetVendSessionState(VendSessionState.VendCommited);
        }

        private void MDBVFailure()
        {
            if (!(this.vendSessionState == VendSessionState.AwaitVendResult))
            {
                SetVendSessionState(VendSessionState.AwaitVendResult);
                // This is a blocking operation.
                RollbackAuthorization();
                // No Data response
                SetMDBBack(true);
            }
        }

        private void MDBVSessionComplete()
        {
            if (vendSessionState != VendSessionState.VendCommited)
            {
                //vend was not commited, product not dispensed, need to rollback
                RollbackAuthorization();
            }

            SetVendSessionState(VendSessionState.Idle);
            SetReaderState(ReaderState.SessionIdle);

            // TODO: Start polling again


            Trace(string.Format("<-- {0}", MDBReaderCommand.END_SESSION), MDBTraceFlag.StateMachine);
            byte[] endSession = new byte[] {(byte) MDBReaderCommand.END_SESSION};
            SetReaderConfig(endSession);
        }

        #endregion

        #region CANCEL VENDING PROCESS

        /// <summary>
        /// Cancel all vending related stuff here and rollback auhtorization transaction.
        /// </summary>
        private void CancelVend()
        {
            byte[] cancelVending = new byte[] { 0x08 };
            SetReaderConfig(cancelVending);

            CancelAllMachine();

            Trace("Vend cancelled.", MDBTraceFlag.HighLevel);
            SetVendSessionState(VendSessionState.VendDenied);

            // No Data response
            SetMDBBack(true);
        }

        /// <summary>
        /// Rolls back the ongoing authorization transaction.
        /// </summary>
        private void RollbackAuthorization()
        {
            if (_posService != null && !_posService.IsDisposed) _posService.Dispose(); //close any open connections

            if (_currentInvoiceNumber != null)
            {
                _posService = new PosService();

                //void the invoice
                bool success = _posService.ProcessVoid(_currentInvoiceNumber);
                if (success)
                {
                    Trace("Transaction rolled back. Voided.", MDBTraceFlag.HighLevel);
                }
                else
                {
                    Trace("Void failed.", MDBTraceFlag.HighLevel);
                }

                _posService.Dispose();
            }

            Trace("Transaction was never authorized.", MDBTraceFlag.HighLevel);

            SetVendSessionState(VendSessionState.Idle);
        }

        #endregion

        #region CANCEL MACHINE PROCESS
        private void CancelAllMachine()
        {
            // Cancel is non blocking
            operation cancelVend = new operation(this.CustomCancelProcess);
            cancelVend.BeginInvoke(null, null);
        }

        private void ResetTime()
        {

            // Reset Time
            if (_currentTransactionTimer != null && _currentTransactionTimer.IsRunning)
            {
                _currentTransactionTimer.Stop();
            }
            _currentTransactionTimer = Stopwatch.StartNew();
        }

        private void CustomCancelProcess() { 
            try
            {
                // Cancel vend session & rollback.
                Trace("Cancel vend session & rollback.", MDBTraceFlag.HighLevel);
                if (_voidQueue != null)
                {
                    _voidQueue = new Queue<Action>();
                    _voidQueue.Enqueue(RollbackAuthorization);
                }
            }
            catch (Exception e)
            {
                Trace("Exception CancelAllMachine.", MDBTraceFlag.HighLevel);
                Console.WriteLine(e);
            }

            try
            {
                // Stop Counter
                Trace("Stop Counter.", MDBTraceFlag.HighLevel);
                ResetTime();
                // Reset Variables In the machine
                Trace("Reset Variables In the machine.", MDBTraceFlag.HighLevel);
                _process = false;
                // Reset Variables
                Trace("Check Dat aReset Machine.", MDBTraceFlag.HighLevel);
                CheckDataResetMachine();
            }
            catch (Exception e)
            {
                Trace("Exception CancelAllMachine.", MDBTraceFlag.HighLevel);
                Console.WriteLine(e);
            }
        }
        #endregion

        #region ACTIVE MACHINE

        private void InitConnectionPOS()
        {
            CloseConnectionPOS();
            _posService = new PosService();
        }

        private void CloseConnectionPOS()
        {
            //close any open connections
            Trace("Maquina Procees CloseConnectionPOS." + _posService, MDBTraceFlag.HighLevel);
            try
            {
                Trace("Maquina Procees CloseConnectionPOS." + _posService.IsDisposed, MDBTraceFlag.HighLevel);
            }
            catch (Exception e)
            {
                Trace("EEEEEEEE POS " + e.Message, MDBTraceFlag.HighLevel);
            }
            if (_posService != null && !_posService.IsDisposed) _posService.Dispose();
        }

        private void ActiveMachine()
        {
            // new card, reset invoice number
            _currentInvoiceNumber = null;

            // Reset Time
            ResetTime();

            // Send Main Status Machine
            SetReaderState(ReaderState.Vend);
            SetVendSessionState(VendSessionState.CardSwiped);
        }

        private void CheckDataResetMachine()
        {
            // show value _process
            Trace(string.Format("Proces Value: {0}", _process), MDBTraceFlag.StateMachine);

            if (_process)
            {
                // this.vendSessionState == VendSessionState.VendRequested
                // Next process
                Trace("START Real Process.", MDBTraceFlag.HighLevel);
            }
            else
            {
                Trace("RESET Real Process.", MDBTraceFlag.HighLevel);
                // Reset Variables
                ResetVariables();
                // Active State
                ActiveMachine();
                // Continue next process
                _process = true;
            }
        }

        private void ResetVariables()
        {
            // Config Machine
            InitConnectionPOS();
            // Reset Value
            InitVariablesMachine();
        }

        #endregion

        /// <summary>
        /// Set New READER Status
        /// </summary>
        /// <param name="newState"></param>
        private void SetReaderState(ReaderState newState)
        {
            ReaderState oldState = this.readerState;
            this.readerState = newState;
            Trace(string.Format("Reader State: {0} -> {1}", oldState, newState), MDBTraceFlag.StateMachine);
        }

        /// <summary>
        /// Set New VEND SESSION Status
        /// </summary>
        /// <param name="newState"></param>
        private void SetVendSessionState(VendSessionState newState)
        {
            VendSessionState oldState = this.vendSessionState;
            this.vendSessionState = newState;
            Trace(string.Format("Vend State: {0} -> {1}", oldState, newState), MDBTraceFlag.StateMachine);
        }

        /// <summary>
        /// Set Reader Config to the vending Machine
        /// </summary>
        /// <param name="data"></param>
        private void SetReaderConfig(byte[] data)
        {
            try
            {
                // Trace(string.Format("SetReaderConfig State"), MDBTraceFlag.HighLevel);
                this.serialDriver.SendMessageAwaitCPAck(CPControlCode.DATA, data);
                // Trace(string.Format("SetReaderConfig Data: {0} -> {1}", CPControlCode.DATA, data), MDBTraceFlag.HighLevel);
            }
            catch (Exception e)
            {
                Trace("Error SetReaderConfig ..." + e.Message, MDBTraceFlag.HighLevel);
            }

           

        }

        #region Trace

        private void serialDriver_MessageTrace(object sender, MessageTraceEventArgs args)
        {
            Trace("SerialDriver", args.Message, MDBTraceFlag.MDB);
        }

        void serialDriver_DetailMessageTrace(object sender, MessageTraceEventArgs args)
        {
            Trace("SerialDriver", args.Message, MDBTraceFlag.MDBDetail);
        }

        private void Trace(string message, MDBTraceFlag traceFlag)
        {
            Trace("CashlessDeviceSimulator", message, traceFlag);
        }

        private void Trace(string prefix, string message, MDBTraceFlag traceFlag)
        {
            Logger.Log("{0}[{1}]:\t{2}", prefix, traceFlag.ToString(), message);
        }

        private void OnMessageTrace(string message)
        {
            if (this.MessageTrace != null)
            {
                MessageTraceEventArgs args = new MessageTraceEventArgs() {Message = message};
                this.MessageTrace(this, args);
            }
        }

        #endregion
    }
}