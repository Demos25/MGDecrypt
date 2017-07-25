﻿using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace MGDecrypt
{
    class Program
    {
        const int KEY_CONST = 0x02E90EDD;
        public int rootEntryLength = 12;
        public int directoryNameLength = 8;
        public int directoryEntryLength = 8;
        static void Main(string[] args)
        {
            Program prog = new Program();
            if (args.Length == 3 && args[2] == "-z")
            {
                prog.Decrypt(args[0], args[1], true);
            } else if (args.Length == 2)
            {
                prog.Decrypt(args[0], args[1]);
            } else
            {
                Console.Write("Usage mgdecrypt infile outfile [-z]");
                Console.Write("-z ZOE2 Decryption");
            }
        }

        public uint HashFolderName(byte[] folderName)
        {
            uint bitmask = 0xffffff;
            int i = 0;
            uint hashed = 0;
            do
            {
                uint hashRight = hashed >> 0x13;
                uint hashLeft = hashed << 0x5;
                hashed = hashLeft | hashRight;
                hashed += folderName[i];
                hashed &= bitmask;
                i++;
            } while (folderName[i] != 0 && i < folderName.Length-1);
            return hashed;
        }

        public uint HashFolderNameZOE(byte[] folderName)
        {
            uint bitmask = 0xf;
            int i = 0;
            uint hashed = 0;
            uint hashed2 = 0;
            uint a0 = 0;
            uint a1 = folderName[0];
            uint a2 = 0;
            do
            {
                hashed = (uint)i & bitmask;
                hashed = a1 << (byte)hashed;
                a0 = a2 >> 3;
                hashed2 = a1 & bitmask;
                a0 += hashed;
                a0 += a1;
                hashed2 = a2 << (byte)hashed2;
                i++;
                a1 = folderName[i];
                hashed2 |= a0;
                a2 += hashed2;
            } while (folderName[i] != 0 && i < folderName.Length - 1);
            return a2;
        }

        public uint MakeFolderKeyX(uint folderHash, uint rootKey)
        {
            uint folderConst = 0xA78925D9;
            uint folderKey = folderHash << 0x7;
            folderKey += rootKey;
            folderKey += folderHash;
            folderKey += folderConst;
            return folderKey;
        }

        public uint MakeFolderKeyY(uint folderHash)
        {
            uint folderConst = 0x7A88FB59;
            uint folderKey = folderHash << 0x7;
            folderKey += folderHash;
            folderKey += folderConst;
            return folderKey;
        }

        public uint DecryptRoutine(uint keyX, uint keyY, int offset, byte[] input, byte[] output)
        {
            for (int i = offset; i < input.Length; i += 4)
            {
                uint interval = keyX * KEY_CONST;
                uint encryptedWord = (uint)BitConverter.ToInt32(input, i);
                encryptedWord ^= keyX;
                byte[] decryptedBytes = BitConverter.GetBytes(encryptedWord);
                decryptedBytes.CopyTo(output, i);
                keyX = interval + keyY;
            }
            return keyX;
        }


        public void Decrypt(string inFilename, string outFilename, bool zoe2 = false)
        {
            //Open bufferedStreams
            if (zoe2)
            {
                  rootEntryLength = 20;
                  directoryNameLength = 16;
                  directoryEntryLength = 12;
             }
            BufferedStream reader = new BufferedStream(File.Open(inFilename, FileMode.Open));
            BufferedStream writer = new BufferedStream(File.Open(outFilename, FileMode.OpenOrCreate));
            byte[] rootTableEncrypted = new byte[16];
            reader.Read(rootTableEncrypted, 0, 16);
            uint rootKey = (uint)BitConverter.ToInt32(rootTableEncrypted, 0);
            uint keyX = rootKey;
            uint keyY = rootKey ^ 0xF0F0;
            byte[] rootTableDecrypted = new byte[16];
            keyX = DecryptRoutine(keyX, keyY, 4, rootTableEncrypted, rootTableDecrypted);
            writer.Write(rootTableDecrypted, 0, rootTableDecrypted.Length);

            int directoryCount = BitConverter.ToInt16(rootTableDecrypted, 8);
            rootTableEncrypted = new byte[directoryCount * rootEntryLength];
            rootTableDecrypted = new byte[directoryCount * rootEntryLength];
            reader.Read(rootTableEncrypted, 0, rootTableEncrypted.Length);
            keyX = DecryptRoutine(keyX, keyY, 0, rootTableEncrypted, rootTableDecrypted);
            writer.Write(rootTableDecrypted, 0, rootTableDecrypted.Length);
            Directory[] directories = new Directory[directoryCount];
            for (int i = 0; i < directoryCount; i++)
            {
                byte[] directoryName = new byte[directoryNameLength];
                for (int j = 0; j < directoryNameLength; j++)
                {
                    directoryName[j] = rootTableDecrypted[(i * rootEntryLength) + j];
                }
                uint folderHash;
                if (zoe2)
                {
                    folderHash = HashFolderNameZOE(directoryName);
                } else
                {
                    folderHash = HashFolderName(directoryName);
                }
                uint folderKeyX = MakeFolderKeyX(folderHash, rootKey);
                uint folderKeyY = MakeFolderKeyY(folderHash);
                uint offset = BitConverter.ToUInt32(rootTableDecrypted, (i * rootEntryLength) + directoryNameLength);
                offset *= 2048;
                directories[i] = new Directory(folderHash, folderKeyX, folderKeyY, System.Text.Encoding.Default.GetString(directoryName).TrimEnd('\0'), offset);
            }

            List<DirectoryFile> fileList = new List<DirectoryFile>();
            for (int i = 0; i < directories.Length; i++)
            {
                reader.Seek(directories[i].offset, SeekOrigin.Begin);
                writer.Seek(directories[i].offset, SeekOrigin.Begin);
            

                byte[] folderEncryptedEntries = new byte[4];
                byte[] folderDecryptedEntries = new byte[4];
                reader.Read(folderEncryptedEntries, 0, 4);
                uint nextKeyX = DecryptRoutine(directories[i].keyX, directories[i].keyY, 0, folderEncryptedEntries, folderDecryptedEntries);
                int tableLength = BitConverter.ToInt32(folderDecryptedEntries,0); 
                byte[] folderTableEncrypted = new byte[tableLength * directoryEntryLength];
                byte[] folderTableDecrypted = new byte[tableLength * directoryEntryLength];
                reader.Read(folderTableEncrypted, 0, tableLength * directoryEntryLength);
                DecryptRoutine(nextKeyX, directories[i].keyY, 0, folderTableEncrypted, folderTableDecrypted);
                byte[] joinedDecryptedTable = new byte[tableLength * directoryEntryLength + 4];
                folderDecryptedEntries.CopyTo(joinedDecryptedTable, 0);
                folderTableDecrypted.CopyTo(joinedDecryptedTable, 4);
                directories[i].SetDirectoryTable(joinedDecryptedTable);
                writer.Write(joinedDecryptedTable, 0, joinedDecryptedTable.Length);

                uint directoryLength;
                if (i < directories.Length - 1)
                {
                    directoryLength = directories[i + 1].offset - directories[i].offset;
                }
                else
                {
                    directoryLength = (uint)reader.Length - directories[i].offset;
                }
                fileList.AddRange(directories[i].GetFilesFromTable(directoryLength, directoryEntryLength));
            }
            //Now we have the list of files iterate through them and if needed decrypt, otherwise copy. Decryption done!!
            for (int i = 0; i < fileList.Count; i++)
                {
                uint wordFileLength = ((uint)Math.Ceiling(fileList[i].length / (double)4)) * 4;
                byte[] fileData = new byte[wordFileLength];
                    reader.Seek(fileList[i].offset, SeekOrigin.Begin);
                    writer.Seek(fileList[i].offset, SeekOrigin.Begin);
                    reader.Read(fileData, 0, fileData.Length);
                    if (fileList[i].crypted)
                    {
                    byte[] decryptedFileData = new byte[wordFileLength];

                    uint fileKey = BitConverter.ToUInt16(fileData, 0);
                    fileKey ^= 0x9385;
                    uint fileKeyY = fileKey * 0x116;
                    uint fileKeyX = fileKey ^ 0x6576;
                    fileKeyX <<= 0x10;
                    fileKeyX |= fileKey;
                    DecryptRoutine(fileKeyX, fileKeyY, 0, fileData, decryptedFileData);
                    decryptedFileData[0] = 0x78;
                    decryptedFileData[1] = 0x9c;
                    writer.Write(decryptedFileData, 0, fileData.Length);
                } else
                    {
                        writer.Write(fileData, 0, fileData.Length);
                    }
                }


            reader.Close();
            writer.Close();
        }
    }

    public class Directory
    {
        public uint hash;
        public uint keyX;
        public uint keyY;
        public uint offset;
        public string folderName;
        byte[] directoryTable;

        public Directory(uint folderHash, uint folderKeyX, uint folderKeyY, string folderName, uint offset)
        {
            this.hash = folderHash;
            this.keyX = folderKeyX;
            this.keyY = folderKeyY;
            this.folderName = folderName;
            this.offset = offset;
        }

        public void SetDirectoryTable(byte[] directoryTable)
        {
            this.directoryTable = directoryTable;
        }

        public List<DirectoryFile> GetFilesFromTable(uint directoryLength, int directoryEntryLength)
        {
            if (directoryTable == null)
            {
                return null;
            }
            List < DirectoryFile > files = new List<DirectoryFile>();
            int tableEntries = BitConverter.ToInt32(directoryTable, 0);
            uint tableSectorLength = ((uint)Math.Ceiling(directoryTable.Length / (double)2048)) * 2048;
            for (int i = 0; i < tableEntries; i++)
            {
                uint fileCheck = BitConverter.ToUInt32(directoryTable, i * directoryEntryLength + 4);
                if (fileCheck >> 24 == 0x7E)
                {
                    uint fOffset = BitConverter.ToUInt32(directoryTable, i * directoryEntryLength + directoryEntryLength) + offset + tableSectorLength;
                    uint fileLength = fileCheck ^ 0x7E000000;
                    files.Add(new DirectoryFile(true, fOffset, fileLength));
                }
            }
            //Get unencrypted data offsets/lengths -- from the sector after the length of the crypted file to the length of the directory,
            //if there is any
            //First if there's crypted data -- I think there always is
            uint uncryptedDataPos;
            if (files.Count > 0)
            {
                uint lastFileSectorLength = ((uint)Math.Ceiling(files[files.Count - 1].length /(double) 2048)) * 2048;
                uint lastFileOffset = (files[files.Count - 1].offset);
                uncryptedDataPos = lastFileOffset + lastFileSectorLength - offset;
            } else
            {
                uncryptedDataPos = tableSectorLength;
            }
            if (uncryptedDataPos < directoryLength)
            {
                uint fOffset = uncryptedDataPos + offset;
                uint fileLength = directoryLength - uncryptedDataPos;
                files.Add(new DirectoryFile(false, fOffset, fileLength));
            }
            return files;
        }
    }

    public class DirectoryFile
    {
        public bool crypted;
        public uint offset;
        public uint length;
        
        public DirectoryFile(bool crypted, uint offset, uint length)
        {
            this.crypted = crypted;
            this.offset = offset;
            this.length = length;

        }
    }
}
