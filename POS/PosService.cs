using Kinpos.comm;
using Kinpos.Dcl;
using Kinpos.Dcl.Core;
using Newtonsoft.Json;
using SOLVEN.Common;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Threading;

namespace POS
{
    /// <summary>
    /// Controls every operation and communication with the external POS.
    /// </summary>
    public class PosService
    {
        /// <summary>
        /// Code identifier for the dollar currency. Used in sale transactions.
        /// </summary>
        private const string DOLLARS = "840";

        /// <summary>
        /// The given default local network IP for the POS.
        /// </summary>
        private const string POS_IP = "192.168.8.126";

        /// <summary>
        /// The default port which the POS is listening to.
        /// </summary>
        /// <remarks>
        /// Should always be 5000.
        /// </remarks>
        private const int POS_PORT = 5000;

        /// <summary>
        /// The default POS communication timeout for this program.
        /// </summary>
        /// <remarks>
        /// The POS timeout for the bank communication is set at 30s. In order
        /// to minimize communication mishaps, our timeout is set to 60s.
        /// </remarks>
        private const int POS_TIMEOUT = 60000;
        //private const int POS_TIMEOUT = 15000; // Documentation

        /// <summary>
        /// The max retries for the <see cref="VerifyLastTransaction"/> method.
        /// </summary>
        private const int MAX_RETRIES = 3;

        /// <summary>
        /// The DCL communication object via TCP/IP (wireless).
        /// </summary>
        private DCL_Ethernet _dcl;

        /// <summary>
        /// The DCL communication object via RS232 serial.
        /// </summary>
        private DCL_RS232 _dclRs232;

        /// <summary>
        /// The current transaction rps identifier.
        /// </summary>
        /// <remarks>
        /// Should be overriden every time a new transaction is initiated.
        /// </remarks>
        private string _currentTransactionRpsId;

        /// <summary>
        /// The random object used to generate a RPS identifier.
        /// </summary>
        private Random _random;

        public bool IsDisposed { get; private set; }

        /// <summary>
        /// Calls the <see cref="InitializeDcl"/> method.
        /// </summary>
        /// <remarks>
        /// This is the only constructor for this service. A new instance of this service
        /// must be created every time the SOLVEN flow initiates.
        /// </remarks>
        public PosService()
        {
            // InitializeDcl();
            // Configuration Wire (RS232)
            InitializeDcl_RS232();
            _random = new Random();
            IsDisposed = false;
        }

        /// <summary>
        /// Initializes the DCL communication with the POS via TCP/IP (wireless).
        /// </summary>
        private void InitializeDcl()
        {
            Trace("Initializing POS DCL...");

            //read DCL config from file
            var path = Path.Combine(Directory.GetCurrentDirectory(), "pos_config.json");
            var json = File.ReadAllText(path);
            var posConfig = JsonConvert.DeserializeObject<PosConfig>(json);

            //verify defaults
            if (string.IsNullOrEmpty(posConfig.IpAddress)) posConfig.IpAddress = POS_IP;
            if (posConfig.Port == 0) posConfig.Port = POS_PORT;
            if (posConfig.Timeout == 0) posConfig.Timeout = POS_TIMEOUT;

            //initialize DCL object
            _dcl = new DCL_Ethernet
            {
                IP = posConfig.IpAddress,
                Port = posConfig.Port,
                Timeout = posConfig.Timeout
            };

            Trace(string.Format("Connected to: {0}", _dcl.IP));
        }

        /// <summary>
        /// Initializes the DCL communication with the POS via RS232 serial.
        /// Integration DCL – Serial
        /// </summary>
        private void InitializeDcl_RS232()
        {
            // Default: /dev/serial
            // Documentation: COM1
            // Other Option: /dev/ttyUSB0
            string CommunPort = "/dev/ttyUSB0";
            _dclRs232 = new DCL_RS232
            {
                StopBits = "1",
                Baudrate = "115200",
                ComPort = CommunPort,
                DataBits = "8",
                Parity = "None",
                Timeout = POS_TIMEOUT,
                // Syncronous
                ProcessBIN = false,
                UseReceiveMethodAsString = true
            };

            // Enable Contac Less option
            _dclRs232.UseContactlessAmount = true;

            Trace(string.Format("Connected to: {0}", _dclRs232.ComPort));
        }

        /// <summary>
        /// Starts a background worker to activate the reader to request a card swipe.
        /// </summary>
        /// <param name="cardSwipedEvent">Card swiped event.</param>
        public void StartRequestCardSwipe(string amount, CardSwipedEventHandler cardSwipedEvent)
        {
            BackgroundWorker bw = new BackgroundWorker();

            // what to do in the background thread
            bw.DoWork += delegate(object o, DoWorkEventArgs args)
            {
                BackgroundWorker b = o as BackgroundWorker;

                var success = RequestSwipe(amount, out string bin);

                CardSwipedEventArgs responseArgs = new CardSwipedEventArgs() {Bin = bin, IsCardSwiped = success};
                args.Result = responseArgs;
            };

            // what to do when worker completes its task (notify the user)
            bw.RunWorkerCompleted += delegate(object o, RunWorkerCompletedEventArgs args)
            {
                CardSwipedEventArgs responseArgs;

                if (args == null || args.Result == null)
                {
                    responseArgs = new CardSwipedEventArgs {IsCardSwiped = false, Bin = null};
                }
                else
                {
                    responseArgs = (CardSwipedEventArgs) args.Result;
                }

                cardSwipedEvent(this, responseArgs);
            };

            bw.RunWorkerAsync();
        }

        /// <summary>
        /// Activates the POS to requests a card swipe synchronously.
        /// </summary>
        /// <remarks>
        /// Should only be called by a background worker to avoid blocking the main thread.
        /// Keep the channel open until the sale is processed.
        /// The DCL library will hold a reference to the swiped card as long as the channel remains open.
        /// </remarks>
        /// <returns><c>true</c>, if swipe was requested, <c>false</c> otherwise.</returns>
        /// <param name="bin">Bin.</param>
        private bool RequestSwipe(string amountString, out string bin)
        {
            Trace("START Requesting card swipe...");

            bin = null;

            _dclRs232.setAmount(amountString);
            _dclRs232.setCurrency(DOLLARS);
            _dclRs232.OpenChannel = true;
            _dclRs232.ClearTransaction = true;

            // Contact less - Config
            _dclRs232.UseContactlessAmount = true;
            _dclRs232.setMerchantId(0x00);
            _dclRs232.setAcquirerId(0x00);

            //wait for the response
            // new List<byte[]> {
            //     DCL_TAG._TxnRspCode , // 0 Código de Respuesta ASCII
            //     DCL_TAG._TxnAuthNum , // 1 Número de Autorización ASCII
            //     DCL_TAG._TxnDate , // 2 Fecha de la transacción BCD (HHMMSS)
            //     DCL_TAG._TxnTime , // 3 Hora de la transacción BCD (AAAAMMDD)
            //     DCL_TAG._TxnTID , // 4 Terminal Id ASCII
            //     DCL_TAG._TxnMerName , // 5 Id del comercio ASCII
            //     DCL_TAG._TxnMaskPAN , // 6 Tarjeta Enmascarada ASCII
            //     DCL_TAG._TxnInvoice , // 7 Factura BCD
            //     DCL_TAG._TxnIssName , // 8 Emisor ASCII
            //     DCL_TAG._TxnEMVDataEND , // 9 Datos EMV para Chip y Contactless BCD
            //     DCL_TAG._TxnRRN , // 10 Referencia ASCII
            //     DCL_TAG._TxnSTAN , // 11 System Trace Number BCD
            //     DCL_TAG._TxnHolderName , // 12 Nombre del Tarjeta Habiente ASCII
            //     DCL_TAG._TxnPosEntryMode, // 13 Entry Mode : 'I' / CHIP , 'C' - Contacless, 'S' - Banda BCD
            //     DCL_TAG._AppVersion , // 14 Version ASCII
            //     DCL_TAG._AppRelease // 15 Release ASCII
            // }
            var DCL_TAG_OUTPUT = new List<byte[]>
            {
                // DCL_TAG._TxnRspCode , // 0 Código de Respuesta ASCII
                // DCL_TAG._TxnAuthNum , // 1 Número de Autorización ASCII
                // DCL_TAG._TxnDate , // 2 Fecha de la transacción BCD (HHMMSS)
                // DCL_TAG._TxnTime , // 3 Hora de la transacción BCD (AAAAMMDD)
                // DCL_TAG._TxnTID , // 4 Terminal Id ASCII
                // DCL_TAG._TxnMerName , // 5 Id del comercio ASCII
                DCL_TAG._TxnMaskPAN, // 6 Tarjeta Enmascarada ASCII
                // DCL_TAG._TxnInvoice , // 7 Factura BCD
                DCL_TAG._TxnIssName, // 8 Emisor ASCII
                // DCL_TAG._TxnEMVDataEND , // 9 Datos EMV para Chip y Contactless BCD
                // DCL_TAG._TxnRRN , // 10 Referencia ASCII
                DCL_TAG._TxnSTAN, // 11 System Trace Number BCD
                DCL_TAG._TxnHolderName, // 12 Nombre del Tarjeta Habiente ASCII
                // DCL_TAG._TxnPosEntryMode, // 13 Entry Mode : 'I' / CHIP , 'C' - Contacless, 'S' - Banda BCD
                // DCL_TAG._AppVersion , // 14 Version ASCII
                // DCL_TAG._AppRelease
            };

            bin = null;

            Trace("Start set currency and values...");
            _dclRs232.OpenChannel = true;
            _dclRs232.UseContactlessAmount = true;
            _dclRs232.ClearTransaction = true;

            Trace("Init Process Get Card...");
            //wait for the response
            DCL_Result dclResult = _dclRs232.GetCardBin(DCL_TAG_OUTPUT);

            Trace(String.Format("CHECK DCL: {0}", dclResult == null ? "null" : "Exist data"));

            if (dclResult == null)
            {
                return false;
            }

            //save logs
            Trace(String.Format("Request: {0}", _dclRs232.getRequestTrace()));
            Trace(String.Format("Response: {0}", _dclRs232.getResponseTrace()));

            bin = dclResult.GetValue_ASCII2String(0);

            Trace(String.Format("BIN Response: {0}", bin == null ? "null" : bin));

            if (bin == null)
            {
                return false;
            }

            return bin.Length > 0; //card swiped
        }

        public bool RequestSale(string amountString, ref string invoiceNumber)
        {
            Trace("Start RequestSale ...");
            Trace(string.Format("Processing sale of {0}...", amountString));

            _dclRs232.setCurrency(DOLLARS);
            _dclRs232.setAmount(amountString);
            _dclRs232.ClearTransaction = false;

            // Contact less - Config
            _dclRs232.UseContactlessAmount = true;
            _dclRs232.setMerchantId(0x00);
            _dclRs232.setAcquirerId(0x00);

            DCL_Result dclResult = _dclRs232.Sale(new List<byte[]>()
            {
                DCL_TAG._TxnRspCode, // 0 Código de Respuesta ASCII
                DCL_TAG._TxnAuthNum, // 1 Número de Autorización ASCII
                DCL_TAG._TxnDate, // 2 Fecha de la transacción BCD (HHMMSS)
                DCL_TAG._TxnTime, // 3 Hora de la transacción BCD (AAAAMMDD)
                DCL_TAG._TxnTID, // 4 Terminal Id ASCII
                DCL_TAG._TxnMerName, // 5 Id del comercio ASCII
                DCL_TAG._TxnMaskPAN, // 6 Tarjeta Enmascarada ASCII
                DCL_TAG._TxnInvoice, // 7 Factura BCD
                DCL_TAG._TxnIssName, // 8 Emisor ASCII
                DCL_TAG._TxnEMVDataEND, // 9 Datos EMV para Chip y Contactless BCD
                DCL_TAG._TxnRRN, // 10 Referencia ASCII
                DCL_TAG._TxnSTAN, // 11 System Trace Number BCD
                DCL_TAG._TxnHolderName, // 12 Nombre del Tarjeta Habiente ASCII
                DCL_TAG._TxnPosEntryMode, // 13 Entry Mode : 'I' / CHIP , 'C' - Contacless, 'S' - Banda BCD
                DCL_TAG._AppVersion, // 14 Version ASCII
                DCL_TAG._AppRelease // 15 Release ASCII
            });

            _dclRs232.CloseChannel();

            //set RPS ID to identify the transaction on our end
            _currentTransactionRpsId = GenerateRpsId();
            _dclRs232.setTransactionRPSId(_currentTransactionRpsId);

            //save logs
            Trace(String.Format("Request: {0}", _dclRs232.getRequestTrace()));
            Trace(String.Format("Response: {0}", _dclRs232.getResponseTrace()));


            if (dclResult == null)
            {
                //possible comm error, check if transaction was processed by comparing RPS IDs.
                var retriesLeft = MAX_RETRIES;
                bool isTransactionProcessed = false;
                while (retriesLeft > 0 && !isTransactionProcessed)
                {
                    isTransactionProcessed = VerifyLastTransaction(out invoiceNumber);
                    Thread.Sleep(3000); //tiny delay to not overload the POS
                    retriesLeft--;
                }

                return isTransactionProcessed;
            }

            string responseCode = dclResult.GetValue_ASCII2String(0);
            if (responseCode.Equals("00"))
            {
                // invoiceNumber = dclResult.GetValue_BCD2String(3);
                // invoiceNumber = dclResult.GetValue_ASCII2String(0);
                // string invoiceNumber = dclResult.GetValue_BCD2String(1);
                // Trace(String.Format("invoiceNumber: {0}", invoiceNumber));
                // invoiceNumber = dclResult.GetValue_BCD2String(3);
                // Trace(String.Format("invoiceNumber: {0}", invoiceNumber));
                invoiceNumber = dclResult.GetValue_ASCII2String(0);
                //invoiceNumber = dclResult.GetValue_BCD2String(3);

                return true; //sale processed
            }

            return false; //sale not processed
        }


        /// <summary>
        /// Authorize a sale transaction for the given amount.
        /// </summary>
        /// <remarks>
        /// This should be used when the customer selects a product, after initally swiping the card. 
        /// This cannot be called without receiving a RequestCardSwipe() response first.
        /// The amount should be formatted with two decimals, and without a decimal point.
        /// For example, 54.99 becomes "5499".
        /// </remarks>
        /// <returns><c>true</c>, if sale was processed, <c>false</c> otherwise.</returns>
        /// <param name="amountString">Amount to authorize.</param>
        /// <param name="invoiceNumber">Invoice number generated by the transaction.</param>
        public bool ProcessSale(string amountString, ref string invoiceNumber)
        {
            Trace(string.Format("Processing sale of {0}...", amountString));

            _dclRs232.setAmount(amountString);
            _dclRs232.setCurrency(DOLLARS);
            _dclRs232.setPrintBeforeSendData(true);
            _dclRs232.ProcessBIN = true;
            _dclRs232.OpenChannel = true;
            _dclRs232.UseContactlessAmount = true;
            _dclRs232.ClearTransaction = false;

            //set RPS ID to identify the transaction on our end
            _currentTransactionRpsId = GenerateRpsId();
            _dclRs232.setTransactionRPSId(_currentTransactionRpsId);

            //wait for the response
            DCL_Result dclResult = _dclRs232.Sale_Step2(null);

            //save logs
            Trace(String.Format("Request: {0}", _dclRs232.getRequestTrace()));
            Trace(String.Format("Response: {0}", _dclRs232.getResponseTrace()));

            if (dclResult == null)
            {
                //possible comm error, check if transaction was processed by comparing RPS IDs.
                var retriesLeft = MAX_RETRIES;
                bool isTransactionProcessed = false;
                while (retriesLeft > 0 && !isTransactionProcessed)
                {
                    isTransactionProcessed = VerifyLastTransaction(out invoiceNumber);
                    Thread.Sleep(3000); //tiny delay to not overload the POS
                    retriesLeft--;
                }

                return isTransactionProcessed;
            }

            string responseCode = dclResult.GetValue_ASCII2String(0);
            if (responseCode.Equals("00"))
            {
                invoiceNumber = dclResult.GetValue_BCD2String(3);
                return true; //sale processed
            }

            return false; //sale not processed
        }


        public bool LecturaTarjeta(List<byte[]> pCmd, ref string pTrack1, ref string pTrack2, ref string pTarjeta,
            ref short pNumErr, ref string pMsgErr)
        {
            bool tarjetaLeida = false;
            // String EMVData = ""; //para uso futuro
            //+-+-+-+-++-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+
            //TASK TODO
            // Task a
            // Mandar el comando de lectura a la terminal
            // Task b
            // Setear la respuesta en los campos de referencia
            //+-+-++-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+
            //==========================================================
            // Task a
            // Mandar el comando de lectura a la terminal
            //==========================================================
            // Init the configuration needley: InitializeDcl_RS232()
            _dclRs232.setBankCode("01");
            DCL_Result result = _dclRs232.PP_InsertCard_SwipeCard_PinBlock(pCmd);
            //=============================================================
            // Task b
            // Setear la respuesta en los campos de referencia
            //=============================================================
            if (result != null)
            {
                pTarjeta = result.GetValue_BCD2String(0).Replace("F", "");
                tarjetaLeida = true;
            }
            else
            {
                pNumErr = -1;
                pMsgErr = "Error de Lectura";
                tarjetaLeida = false;
            }

            return tarjetaLeida;
        }


        /// <summary>
        /// Processes a void transaction for the specified invoice.
        /// </summary>
        /// <remarks>
        /// This should be used every time the Vending Machine fails to consume a product, 
        /// working as a refund mechanism to avoid customer complaints.
        /// </remarks>
        /// <returns><c>true</c>, if void was processed, <c>false</c> otherwise.</returns>
        /// <param name="invoiceNumber">Invoice number to void.</param>
        public bool ProcessVoid(string invoiceNumber)
        {
            Trace(string.Format("Processing void for {0}...", invoiceNumber));

            _dclRs232.OpenChannel = true;
            _dclRs232.setInvoice(invoiceNumber);

            //set RPS ID to identify the transaction on our end
            _currentTransactionRpsId = GenerateRpsId();
            _dclRs232.setTransactionRPSId(_currentTransactionRpsId);

            //wait for the response
            DCL_Result dclResult = _dclRs232.Void(null);

            //save logs
            Trace(String.Format("Request: {0}", _dclRs232.getRequestTrace()));
            Trace(String.Format("Response: {0}", _dclRs232.getResponseTrace()));

            if (dclResult == null)
            {
                //possible comm error, check if transaction was processed by comparing RPS IDs.
                var retriesLeft = MAX_RETRIES;
                bool isTransactionProcessed = false;
                while (retriesLeft > 0 && !isTransactionProcessed)
                {
                    isTransactionProcessed = VerifyLastTransaction();
                    Thread.Sleep(3000); //tiny delay to not overload the POS
                    retriesLeft--;
                }

                return isTransactionProcessed;
            }

            string responseCode = dclResult.GetValue_ASCII2String(0);
            return responseCode.Equals("00");
        }

        /// <summary>
        /// Verifies whether the last transaction has been approved by the bank.
        /// </summary>
        /// <remarks>
        /// This is called by each transaction in case that the DCL_Result object is null, 
        /// which means that there is a slim possibility the bank received the transaction, 
        /// but our program was already timed out.
        /// </remarks>
        /// <returns><c>true</c>, if last transaction was approved, <c>false</c> otherwise.</returns>
        public bool VerifyLastTransaction(out string invoiceNumber)
        {
            Trace(string.Format("Verifying last transaction ({0})...", _currentTransactionRpsId));

            _dclRs232.OpenChannel = true;

            DCL_Result dclResult = _dclRs232.GetLastTransactionInfo(new List<byte[]>
            {
                DCL_TAG._TxnRspCode,
                DCL_TAG._TxnRPS_RecordID,
                DCL_TAG._TxnInvoice
            });

            //save logs
            Trace(String.Format("Request: {0}", _dclRs232.getRequestTrace()));
            Trace(String.Format("Response: {0}", _dclRs232.getResponseTrace()));

            if (dclResult != null)
            {
                string responseCode = dclResult.GetValue_ASCII2String(0);
                if (responseCode.Equals("00"))
                {
                    string rpsId = dclResult.GetValue_BCD2String(1);
                    Trace(string.Format("Retrieved RPS ID: {0}", rpsId));
                    invoiceNumber = dclResult.GetValue_BCD2String(2); //todo verify
                    return rpsId.Equals(_currentTransactionRpsId);
                }
            }

            invoiceNumber = null;
            return false;
        }

        public bool VerifyLastTransaction()
        {
            return VerifyLastTransaction(out string ignored);
        }

        /// <summary>
        /// Generates a random rps identifier used for each transaction.
        /// </summary>
        /// <returns>The rps identifier.</returns>
        private string GenerateRpsId()
        {
            if (_random == null) _random = new Random();

            return _random.Next(0, 9999).ToString("D4");
        }

        /// <summary>
        /// Closes the communication port between this software and the POS.
        /// </summary>
        /// <remarks>Call <see cref="Dispose"/> when you are finished using the <see cref="T:SOLVEN.PosService"/> 
        /// and all of the needed transactions have been made. 
        /// The <see cref="Dispose"/> method leaves the <see cref="T:SOLVEN.PosService"/> in an unusable state. After
        /// calling <see cref="Dispose"/>, you must release all references to the <see cref="T:SOLVEN.PosService"/> so
        /// the garbage collector can reclaim the memory that the <see cref="T:SOLVEN.PosService"/> was occupying.</remarks>
        public void Dispose()
        {
            IsDisposed = true;

            _dclRs232.CloseChannel();
            _dclRs232 = null;
            _currentTransactionRpsId = null;
            _random = null;

            Trace("POS channel closed");
        }

        private void Trace(string message)
        {
            Logger.Log("PosService:\t{0}", message);
        }

        public bool ShowSingleMessage(out bool pressKey, out bool exitProcess)
        {
            pressKey = false;
            exitProcess = false;

            bool check = false;
            // Split the message is two words by group
            String pCmd = "0101 SELECCIONE UN PRODUCTO|0203 PRESIONE CUALQUIER TECLA|0205 PARA CONTINUAR|0206 GRACIAS ";

            _dclRs232.OpenChannel = true;
            _dclRs232.ClearTransaction = true;

            DCL_Result returnedData = _dclRs232.DisplayMessage(pCmd);
            _dclRs232.CloseChannel();

            if (null != returnedData)
            {
                if (null != returnedData.GetValue(0))
                {
                    byte[] keyData = returnedData.GetValue(0);

                    //              -- 0x1C         -> Tecla ENTER 
                    //              -- 0xDF         -> Tecla Cancel 
                    //              -- 0x00 - 0x09  -> tecla del 0 - 9 
                    byte keyPressed = keyData[keyData.Length - 1];


                    // Select KEY
                    switch (keyPressed)
                    {
                        case 0x1C: // Enter
                        case 0x01: // 1
                        case 0x02: // 2
                        case 0x03: // 3
                        case 0x04: // 4
                        case 0x05: // 5
                        case 0x06: // 6
                        case 0x07: // 7
                        case 0x08: // 8
                        case 0x09: // 9
                            pressKey = true;
                            exitProcess = false;
                            // Press Any Key
                            check = true;
                            break;
                        case 0xDF:
                            // Exist (Key CANCEL)
                            exitProcess = true;
                            pressKey = false;
                            // Press Any Key
                            check = true;
                            break;
                    }
                }
            }

            return check;
        }

        /**
         * Manda a pintar una caja de texto en pantalla para entrada de datos.
         * Tener en cuenta los parámetros:
         * a. Longitud Mínima
         * b. Longitud Máxima
         * c. Es Alfanumérico , número
         * d. Nombre del parámetro de salida donde viene el buffer almacenado
         * e. Números de decimales
         * f. Prefijo a mostrar
         */
        public bool IngresoNumerico(string pCmd)
        {
            bool enteredNumber = false;
            try
            {
                _dclRs232.ProcessBIN = false;
                _dclRs232.setCurrency(DOLLARS);
                _dclRs232.OpenChannel = true;
                _dclRs232.ClearTransaction = false;
                String tagValue = "CV";
                int longitudMinima = 1;
                int longitudMaximma = 400;
                int espaciosDecimales = 0;
                string prefijo = "X";
                DCL_Result returnedData = _dclRs232.Input(
                    pCmd,
                    tagValue,
                    longitudMinima,
                    longitudMaximma,
                    espaciosDecimales,
                    prefijo);

                Trace(String.Format("Request: {0}", _dclRs232.getRequestTrace()));
                Trace(String.Format("Response: {0}", _dclRs232.getResponseTrace()));

                if (returnedData != null)
                {
                    Trace("Input: " + returnedData.ToString());

                    String value = returnedData.GetValue_BCD2String(0);
                    if (!String.IsNullOrEmpty(value))
                    {
                        String tag = value.Substring(0, 4);
                        tag = ASCIIEncoding.ASCII.GetString(Kinpos.Dcl.Util.Utilidad.Str2bcd(tag,
                            false));
                        String length = value.Substring(4, 4);
                        String valorAscii = value.Substring(8, Convert.ToInt32(length) * 2);
                        String valor =
                            ASCIIEncoding.ASCII.GetString(Kinpos.Dcl.Util.Utilidad.Str2bcd(valorAscii, false));
                        Trace("Input VALUE: " + valor.ToString());

                        enteredNumber = true;
                    }
                }
                else
                {
                    enteredNumber = false;
                }
            }
            catch (Exception ex)
            {
                enteredNumber = false;
                Debug.Print(ex.Message);
            }

            return enteredNumber;
        }
    }
}