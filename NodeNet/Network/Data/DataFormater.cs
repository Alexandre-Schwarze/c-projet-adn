﻿using System;
using System.IO;
using System.IO.Compression;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Formatters.Binary;


namespace NodeNet.Network.Data
{
    public class DataFormater
    {
        /// <summary>
        /// Compresse un tableau d'octets vers un nouveau tableau d'octets.
        /// </summary>
        public static byte[] Compress(byte[] raw)
        {
            using (MemoryStream memory = new MemoryStream())
            {
                using (GZipStream gzip = new GZipStream(memory,
                    CompressionMode.Compress, true))
                {
                    gzip.Write(raw, 0, raw.Length);
                }
                return memory.ToArray();
            }
        }

        /// <summary>
        /// Decompresse un tableau d'octets vers un nouveau tableau d'octets.
        /// </summary>
        public static byte[] Decompress(byte[] gzip)
        {
            using (GZipStream stream = new GZipStream(new MemoryStream(gzip),
                CompressionMode.Decompress))
            {
                const int size = 4096;
                byte[] buffer = new byte[size];
                using (MemoryStream memory = new MemoryStream())
                {
                    int count = 0;
                    do
                    {
                        count = stream.Read(buffer, 0, size);
                        if (count > 0)
                        {
                            memory.Write(buffer, 0, count);
                        }
                    }
                    while (count > 0);
                    return memory.ToArray();
                }
            }
        }

        /// <summary>
        /// Sérialize un objet générique dans un tableau d'octets.
        /// </summary>
        public static byte[] Serialize(object obj)
        {
            if (obj == null)
                return null;

            BinaryFormatter bf = new BinaryFormatter();
            try
            {
                using (MemoryStream ms = new MemoryStream())
                {
                    bf.Serialize(ms, obj);
                    Console.WriteLine("Serialized Array Length : " + ms.Length);
                    byte[] compressed = Compress(ms.ToArray());
                    Console.WriteLine("Serialized and Compressed Array Length : " + compressed.Length);
                    return compressed;
                }
            }
            catch (SerializationException ex)
            {
                Console.WriteLine("Serialize Error : " + ex);
                return null;
            }
        }

        /// <summary>
        /// Désérialize un tableau d'octets en un objet générique.
        /// </summary>
        public static T Deserialize<T>(byte[] data)
        {
            object obj = null;
            try
            {
                byte[] uncompressed = Decompress(data);
                BinaryFormatter bf = new BinaryFormatter();
                using (MemoryStream ms = new MemoryStream(uncompressed))
                {
                    obj = bf.Deserialize(ms);
                }
            }
            catch (SerializationException ex)
            {
                Console.WriteLine("Deserialize Error : " + ex);
            }

            return (T)obj;
        }
    }
}