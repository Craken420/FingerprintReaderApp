using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;

namespace SHM.BuroDigital.FolderNewClient
{
    class WSQ
    {
        #region PRIVATE COMPRESSION IMPORTS - dpfj.h and compression.h
        //Call this to intialize the compression library.
        [DllImport("dpfj.dll", SetLastError = true, CallingConvention = CallingConvention.StdCall)]
        private extern static int dpfj_start_compression();

        //Call this to set the maximum size of expected compressed data.
        [DllImport("dpfj.dll", SetLastError = true, CallingConvention = CallingConvention.StdCall)]
        private extern static int dpfj_set_wsq_size(
            uint size,
            uint tolerance_aw);

        //Call this to specify the compression bit rate (bits per pixel or bits output per 8 bits input).  
        [DllImport("dpfj.dll", SetLastError = true, CallingConvention = CallingConvention.StdCall)]
        private extern static int dpfj_set_wsq_bitrate(
            uint bitrate_x100,
            uint tolerance_aw);


        //Call this to compress the FID.  Compressed data is returned via dpfj_get_processed_data.
        [DllImport("dpfj.dll", SetLastError = true, CallingConvention = CallingConvention.StdCall)]
        private extern static int dpfj_compress_raw(
            byte[] image_data,
            int image_size,
            int image_width,
            int image_height,
            int image_dpi,
            int image_bpp,
            int compression_alg);


        //Call this to compress the FID.  Compressed data is returned via dpfj_get_processed_data.
        [DllImport("dpfj.dll", SetLastError = true, CallingConvention = CallingConvention.StdCall)]
        private extern static int dpfj_compress_fid(
            int fid_type,
            byte[] fid,
            int fid_size,
            int compression_alg);


        //Call this to uncompress the data and retreive the raw pixel data (not in ISO\ANSI image format)
        //Call dpfj_finish_compression to release the resources associated with this operation.
        [DllImport("dpfj.dll", SetLastError = true, CallingConvention = CallingConvention.StdCall)]
        private extern static int dpfj_expand_raw(
            byte[] image_data,
            int image_size,
            int compression_alg,
            int image_width,
            int image_height,
            int image_dpi,
            int image_bpp);

        //Call this to uncompress the data into a specific ISO\ANSI image format.  
        //Call dpfj_finish_compression to release the resources associated with this operation.
        [DllImport("dpfj.dll", SetLastError = true, CallingConvention = CallingConvention.StdCall)]
        private extern static int dpfj_expand_fid(
            int fid_type,
            byte[] fid,
            uint fid_size,
            int compression_alg);

        //Call this method to fill in image_data with the compressed data.  This method can optionally be used
        //to request the required buffer size for efficient allocation.
        [DllImport("dpfj.dll")]
        private extern static int dpfj_get_processed_data(
             byte[] image_data,
            ref int image_size);


        //Call this to release unamaged releases related to the compression operation
        [DllImport("dpfj.dll", SetLastError = true, CallingConvention = CallingConvention.StdCall)]
        private extern static int dpfj_finish_compression();
        #endregion

        #region PUBLIC COMPRESSION ROUTINES
        public static readonly int DPFJ_SUCCESS = 0;
        public static readonly int DPFJ_COMPRESSION_WSQ_NIST = 1;
        public static readonly int DPFJ_COMPRESSION_WSQ_AWARE = 2;
        public enum IMAGE_FORMAT { DPFJ_FID_ANSI_381_2004 = 1770497, DPFJ_FID_ISO_19794_4_2005 = 16842759 };


        //Uncompress the image data that was compressed by NIST
        public static byte[] UnCompressNIST(byte[] compressedImage, IMAGE_FORMAT format)
        {
            byte[] returnBytes = null;
            int result = dpfj_start_compression();
            if (result == DPFJ_SUCCESS)
            {
                result = dpfj_expand_fid((int)format, compressedImage, (uint)compressedImage.Length, DPFJ_COMPRESSION_WSQ_NIST);
                if (result == DPFJ_SUCCESS)
                {
                    int resultImageSize = 0;

                    result = dpfj_get_processed_data(null, ref resultImageSize); //Get size of data to be returned
                    if (result == 0x5BA000D && resultImageSize > 0)  //More data, call method and give buffer
                    {
                        returnBytes = new byte[resultImageSize];
                        result = dpfj_get_processed_data(returnBytes, ref resultImageSize);
                    }
                }
            }
            dpfj_finish_compression();
            return returnBytes;
        }

        //Call this to uncompress AWARE image data
        public static byte[] UnCompressAWARE(byte[] compressedImage, IMAGE_FORMAT format)
        {
            byte[] returnBytes = null;
            int result = dpfj_start_compression();
            if (result == DPFJ_SUCCESS)
            {
                result = dpfj_expand_fid((int)format, compressedImage, (uint)compressedImage.Length, DPFJ_COMPRESSION_WSQ_AWARE);
                if (result == DPFJ_SUCCESS)
                {
                    int resultImageSize = 0;

                    result = dpfj_get_processed_data(null, ref resultImageSize); //Get size of data to be returned
                    if (result == 0x5BA000D && resultImageSize > 0)  //More data, call method and give buffer
                    {
                        returnBytes = new byte[resultImageSize];
                        result = dpfj_get_processed_data(returnBytes, ref resultImageSize);
                    }
                }
            }
            dpfj_finish_compression();
            return returnBytes;
        }

        //Call this to compress the FID captured from the UareU SDK.  
        public static byte[] CompressNIST(DPUruNet.Fid image, uint bitRate, uint maxSize)
        {
            byte[] newBytes = null;
            int result = dpfj_start_compression();
            if (result == DPFJ_SUCCESS)
            {
                result = dpfj_set_wsq_bitrate(bitRate, 1);
                

                if (result == DPFJ_SUCCESS)
                {
                    result = dpfj_set_wsq_size(maxSize, 1);
                    if (result == DPFJ_SUCCESS)
                    {
                        result = dpfj_compress_fid((int)image.Format, image.Bytes, image.Bytes.Length, DPFJ_COMPRESSION_WSQ_NIST);
                        if (result == DPFJ_SUCCESS)
                        {
                            int resultImageSize = 0;

                            result = dpfj_get_processed_data(null, ref resultImageSize); //Get size of data to be returned
                            if (result == 0x5BA000D && resultImageSize > 0)  //More data, call method and give buffer
                            {
                                newBytes = new byte[resultImageSize];
                                result = dpfj_get_processed_data(newBytes, ref resultImageSize);
                            }

                        }
                        dpfj_finish_compression();
                    }
                }
            }

            if (result == DPFJ_SUCCESS)
            {
                return newBytes;
            }
            return null;
        }

        public static byte[] CompressAWARE(DPUruNet.Fid image, uint bitRate, uint maxSize, uint tolerance_aw = 1)
        {
            byte[] newBytes = null;
            int result = dpfj_start_compression();
            if (result == DPFJ_SUCCESS)
            {
                result = dpfj_set_wsq_bitrate(bitRate, 0);
                if (result == DPFJ_SUCCESS)
                {
                    result = dpfj_set_wsq_size(maxSize, 1);
                    if (result == DPFJ_SUCCESS)
                    {
                        result = dpfj_compress_fid((int)image.Format, image.Bytes, image.Bytes.Length, DPFJ_COMPRESSION_WSQ_AWARE);
                        if (result == DPFJ_SUCCESS)
                        {
                            int resultImageSize = 0;

                            result = dpfj_get_processed_data(null, ref resultImageSize); //Get size of data to be returned
                            if (result == 0x5BA000D && resultImageSize > 0)  //More data, call method and give buffer
                            {
                                newBytes = new byte[resultImageSize];
                                result = dpfj_get_processed_data(newBytes, ref resultImageSize);
                            }

                        }
                        dpfj_finish_compression();
                    }
                }
            }

            if (result == DPFJ_SUCCESS)
            {
                return newBytes;
            }
            return null;
        }
        #endregion
    }
}
