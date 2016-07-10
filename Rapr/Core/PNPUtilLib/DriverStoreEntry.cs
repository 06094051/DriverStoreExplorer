using System;
using System.Collections.Generic;
using System.Globalization;

namespace Rapr.Utils
{
    /// <summary>
    /// Data fields retrieved from Driver store for each driver
    /// </summary>
    public struct DriverStoreEntry
    {
        /// <summary>
        /// Name of the OEM INF in driver store
        /// </summary>
        public string DriverPublishedName;

        /// <summary>
        /// Driver Original INF Name
        /// </summary>
        public string DriverInfName;

        /// <summary>
        /// Driver package provider
        /// </summary>
        public string DriverPkgProvider;

        /// <summary>
        /// Driver class (ex., "System Devices")
        /// </summary>
        public string DriverClass;

        /// <summary>
        /// Sys file date
        /// </summary>
        public DateTime DriverDate;

        /// <summary>
        /// Sys file version
        /// </summary>
        public Version DriverVersion;

        /// <summary>
        /// Signer name. Empty if not WHQLd. 
        /// </summary>
        public string DriverSignerName;

        /// <summary>
        /// Estimated driver size on disk.
        /// </summary>
        public long DriverSize;

        /// <summary>
        /// Field count
        /// </summary>
        private const int FIELD_COUNT = 6;

        public int GetFieldCount()
        {
            return FIELD_COUNT;
        }

        public string[] GetFieldNames()
        {
            return new String[] {
                "INF",
                "Package Provider",
                "Driver Class",
                "Driver Date",
                "Driver Version",
                "Driver Signer"};
        }

        public string[] GetFieldValues()
        {
            List<string> fieldValues = new List<string>();

            fieldValues.Add(this.DriverPublishedName);
            fieldValues.Add(this.DriverPkgProvider);
            fieldValues.Add(this.DriverClass);
            fieldValues.Add(this.DriverDate.ToString("d", CultureInfo.InvariantCulture));
            fieldValues.Add(this.DriverVersion.ToString());
            fieldValues.Add(this.DriverSignerName);

            return fieldValues.ToArray();
        }

        // Returns the human-readable file size for an arbitrary, 64-bit file size 
        // The default format is "0.### XB", e.g. "4.2 KB" or "1.434 GB"
        public static string GetBytesReadable(long i)
        {
            // Get absolute value
            long absolute_i = (i < 0 ? -i : i);
            // Determine the suffix and readable value
            string suffix;
            double readable;

            if (absolute_i >= 0x40000000) // Gigabyte
            {
                suffix = "GB";
                readable = (i >> 20);
            }
            else if (absolute_i >= 0x100000) // Megabyte
            {
                suffix = "MB";
                readable = (i >> 10);
            }
            else if (absolute_i >= 0x400) // Kilobyte
            {
                suffix = "KB";
                readable = i;
            }
            else
            {
                return "1 KB";
            }

            // Divide by 1024 to get fractional value
            readable = (readable / 1024);

            // Return formatted number with suffix
            return readable.ToString("0 ") + suffix;
        }
    };
}
