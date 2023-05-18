using System;

namespace Abrantix.MDB2Serial.Common
{
    public class MDBAmountUtil
    {
        private int numberOfDecimalDigits;
        private int scaleFactor;

        public int NumberOfDecimalDigits
        {
            get { return numberOfDecimalDigits; }
        }

        public int ScaleFactor
        {
            get { return scaleFactor; }
        }

        /// <summary>
        /// This will scale and unscale the amounts according to MDB 4.2, section 7.4.2, Reader Response. 
        /// You can either set the scale factor or the numberOfDigits, but not both. Setting both will cause loss of data.
        /// </summary>
        /// <param name="scaleFactor">The sacle factor. If not used, set to 1.</param>
        /// <param name="numberOfDecimalDigits">The number of decimal digits. Determines where the seperator will be placed during the unscale operation. If not used, set to 0.</param>
        public MDBAmountUtil(int scaleFactor, int numberOfDecimalDigits)
        {
            if ((scaleFactor != 1) && (numberOfDecimalDigits != 0))
            {
                throw new ArgumentException("Invalid MDB amount configuration. MDBAmountScaleFactor must be 1 if MDBNumberOfDecimalDigits is any other than 0.");
            }
            if (scaleFactor > 0xff)
            {
                throw new ArgumentException("Invalid MDB amount configuration. MDBAmountScaleFactor cannot be greater than 255.");
            }
            if (numberOfDecimalDigits > 0xff)
            {
                throw new ArgumentException("Invalid MDB amount configuration. MDBNumberOfDecimalDigits cannot be greater than 255.");
            }
            if (scaleFactor != 1)
            {
                if (scaleFactor == 0)
                {
                    throw new ArgumentException("MDBAmountScaleFactor cannot be 0. Set it to 1 if it is not used.");
                }

                if ((scaleFactor % 10) != 0)
                {
                    throw new ArgumentException("MDBAmountScaleFactor must be a multiple of 10.");
                }
            }

            this.scaleFactor = scaleFactor;
            this.numberOfDecimalDigits = numberOfDecimalDigits;
        }

        /// <summary>
        /// Returns the scaled amount as byte array. This byte array can be used as is in an MDB message.
        /// </summary>
        /// <param name="amount">The amount to be scaled.</param>
        /// <param name="cap">If true, and if the amount is bigger than 0xFFFE, the scalled amount will be capped: set to 0xFFFE. 
        /// If false and the amount is bigger than 0xFFFE, an exception will be thrown.</param>
        /// <returns>The scaled amount as byte array.</returns>
        public byte[] ScaleAmount(decimal amount, bool cap)
        {
            // Mathematically and financially this can be a problem since this will force a round down. It is essential that the configuration is correct.
            var scaledAmount = (int)(amount / scaleFactor * (long)Math.Pow(10, numberOfDecimalDigits));

            if (scaledAmount > 0xFFFE)
            {
                if (cap)
                {
                    scaledAmount = 0xFFFE;
                }
                else
                {
                    throw new ArgumentException(
                        string.Format(
                            "Cannot scale amount {0} for scale factor {1} and {2} decimal digits. The resulting scaled amount is {3}, which does not fit into two bytes.",
                            amount, scaleFactor, numberOfDecimalDigits, scaledAmount));
                }
            }

            var mdbAmount = new byte[2];
            mdbAmount[0] = (byte)(scaledAmount >> 8);
            mdbAmount[1] = (byte)(scaledAmount >> 0);

            return mdbAmount;
        }

        public byte[] ScaleAmount(decimal amount)
        {
            return ScaleAmount(amount, false);
        }

        public decimal UnscaleAmount(byte[] amount)
        {
            decimal scaledAmount = 0;

            if ((amount != null) && (amount.Length > 0))
            {
                scaledAmount = amount[0] << 8;
                if (amount.Length > 1)
                {
                    scaledAmount = scaledAmount + amount[1];
                }
            }

            decimal unscaledAmount = scaledAmount * scaleFactor / (long)Math.Pow(10, numberOfDecimalDigits);

            return unscaledAmount;
        }
    }
}
