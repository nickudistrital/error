using System;

namespace SOLVEN.Common
{
    public static class Extensions
    {
        public static string FormatNoDecimalPoint(this double amount)
        {
            //round to two decimals
            amount = Math.Round(amount, 2);
            //convert to formatted string
            string amountString = amount.ToString("00.00");

            //send the amount without a decimal point to the POS
            return amountString.Replace(".", "").Replace(",", "");
        }

        public static string ToStringAmount(this byte[] amountBytes)
        {
            if (amountBytes.Length != 2) return null;

            int amount = (amountBytes[0] << 8) + (amountBytes[1] << 0);

            return amount.ToString();
        }
    }
}
