﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;

using librpc;

namespace PlayEngine.Helpers {
   public class Memory {
      public class Sections {
         public static List<librpc.MemorySection> getMemorySections(librpc.ProcessInfo processInfo, VM_PROT protection = VM_PROT.READ) {
            var listMemoryEntries = new List<librpc.MemorySection>();
            foreach (var memoryEntry in processInfo.memorySections)
               if ((memoryEntry.protection & protection) == protection)
                  listMemoryEntries.Add(memoryEntry);

            return listMemoryEntries;
         }
      }

      public enum CompareType {
         None,
         ExactValue,
         FuzzyValue,
         IncreasedValue,
         IncreasedValueBy,
         DecreasedValue,
         DecreasedValueBy,
         BiggerThan,
         SmallerThan,
         ChangedValue,
         UnchangedValue,
         BetweenValues,
         UnknownInitialValue
      }
      public static class CompareUtil {
         private static Single oldSearchValue = 0.0f;

         public static Boolean compare(dynamic searchValue, dynamic memoryValueToCompare, CompareType compareType, dynamic[] extraParams = null) {
            CompareUtil.oldSearchValue = searchValue;
            switch (compareType) {
               case CompareType.ExactValue:
                  return searchValue == memoryValueToCompare;
               case CompareType.FuzzyValue:
                  return Math.Abs(searchValue - memoryValueToCompare) < 1.0f;
               case CompareType.IncreasedValue:
                  return memoryValueToCompare > oldSearchValue;
               case CompareType.IncreasedValueBy:
                  return memoryValueToCompare == oldSearchValue + searchValue;
               case CompareType.DecreasedValue:
                  return memoryValueToCompare < oldSearchValue;
               case CompareType.DecreasedValueBy:
                  return memoryValueToCompare == oldSearchValue - searchValue;
               case CompareType.BiggerThan:
                  return searchValue > memoryValueToCompare;
               case CompareType.SmallerThan:
                  return searchValue < memoryValueToCompare;
               case CompareType.ChangedValue:
                  return memoryValueToCompare != oldSearchValue;
               case CompareType.UnchangedValue:
                  return memoryValueToCompare == oldSearchValue;
               case CompareType.BetweenValues:
                  return (memoryValueToCompare >= extraParams[0]) && (memoryValueToCompare <= extraParams[1]);
               case CompareType.None:
               case CompareType.UnknownInitialValue:
               default:
                  return true;
            }
         }
      }

      private static Mutex mutex = new Mutex();
      public static PS4RPC ps4RPC = null;
      public static Boolean initPS4RPC(String ipAddress) {
         try {
            mutex.WaitOne();
            if (ps4RPC != null)
               ps4RPC.Disconnect();
            ps4RPC = new PS4RPC(ipAddress);
            ps4RPC.Connect();
         } catch {
         } finally {
            mutex.ReleaseMutex();
         }
         return ps4RPC != null;
      }
      public static librpc.ProcessInfo getProcessInfoFromName(String processName) {
         return Memory.ps4RPC.GetProcessInfo(Memory.ps4RPC.GetProcessList().processes.Where(proc => proc.name == processName).First().pid);
      }

      public static Byte[] readByteArray(Int32 procId, UInt64 address, Int32 size) {
         Byte[] returnBuf = null;
         try {
            mutex.WaitOne();
            returnBuf = ps4RPC.ReadMemory(procId, address, size);
         } catch (Exception ex) {
            Console.WriteLine("Error during ReadByteArray:\r\nAddress: {0}, Size: {1}\r\n{2}",
                address.ToString("X"), size, ex.ToString());
         } finally {
            mutex.ReleaseMutex();
         }
         return returnBuf ?? new Byte[1];
      }
      public static String readString(Int32 procId, UInt64 address) {
         String returnStr = String.Empty;
         try {
            mutex.WaitOne();
            returnStr = ps4RPC.ReadString(procId, address);
         } catch (Exception ex) {
            Console.WriteLine("Error during ReadString:\r\nEncoding: {0}, Address: {1}\r\n{2}",
                "ASCII", address.ToString("X"), ex.ToString());
         } finally {
            mutex.ReleaseMutex();
         }
         return returnStr;
      }
      public static Object read(Int32 procId, UInt64 address, Type valueType) {
         if (valueType == typeof(String))
            return readString(procId, address);
         return readByteArray(procId, address, valueType == typeof(Boolean) ? 1 : Marshal.SizeOf(valueType))
             .getObject(valueType);
      }

      public static void writeByteArray(Int32 procId, UInt64 address, Byte[] bytes) {
         try {
            mutex.WaitOne();
            ps4RPC.WriteMemory(procId, address, bytes);
         } catch (Exception ex) {
            Console.WriteLine("Error during WriteByteArray:\r\nAddress: {0}, bytes.Length: {1}\r\n{2}",
                   address.ToString("X"), bytes.Length, ex.ToString());
         } finally {
            mutex.ReleaseMutex();
         }
      }
      public static void writeString(Int32 procId, UInt64 address, String str) {
         try {
            mutex.WaitOne();
            ps4RPC.WriteString(procId, address, str);
         } catch (Exception ex) {
            Console.WriteLine("Error during WriteString:\r\nEncoding: {0}, Address: {1}, String: {2}\r\n{3}",
                "ASCII", address.ToString("X"), str, ex.ToString());
         } finally {
            mutex.ReleaseMutex();
         }
      }
      public static void write(Int32 procId, UInt64 address, Object value, Type valueType) {
         if (valueType == typeof(String))
            writeString(procId, address, (String)value);
         else
            writeByteArray(procId, address, value.getBytes(valueType));
      }

      public static List<UInt32> scan(Byte[] scanSearchBuffer, Byte[] scanValueBuffer, Type scanValueType, CompareType scanCompareType, dynamic[] extraParams = null) {
         List<UInt32> listResults = new List<UInt32>();
         dynamic searchValue = null;
         Int32 objectTypeSize = 0;
         Func<Int32 /* scanSearchBufferIndex */, dynamic /* returnValue */> fnGetMemoryValue = null;

         switch (Type.GetTypeCode(scanValueType)) {
            case TypeCode.Byte: {
               searchValue = scanValueBuffer[0];
               fnGetMemoryValue = (iBuffer) => scanValueBuffer[iBuffer];
               objectTypeSize = sizeof(Byte);
            }
            break;
            case TypeCode.Boolean: {
               searchValue = BitConverter.ToBoolean(scanValueBuffer, 0);
               fnGetMemoryValue = (iBuffer) => BitConverter.ToBoolean(scanSearchBuffer, iBuffer);
               objectTypeSize = 1;
            }
            break;
            case TypeCode.Double: {
               searchValue = BitConverter.ToDouble(scanValueBuffer, 0);
               fnGetMemoryValue = (iBuffer) => BitConverter.ToDouble(scanSearchBuffer, iBuffer);
               objectTypeSize = sizeof(Double);
            }
            break;
            case TypeCode.Single: {
               searchValue = BitConverter.ToSingle(scanValueBuffer, 0);
               fnGetMemoryValue = (iBuffer) => BitConverter.ToSingle(scanSearchBuffer, iBuffer);
               objectTypeSize = sizeof(Single);
            }
            break;
            case TypeCode.UInt16: {
               searchValue = BitConverter.ToUInt16(scanValueBuffer, 0);
               fnGetMemoryValue = (iBuffer) => BitConverter.ToUInt16(scanSearchBuffer, iBuffer);
               objectTypeSize = sizeof(UInt16);
            }
            break;
            case TypeCode.Int16: {
               searchValue = BitConverter.ToInt16(scanValueBuffer, 0);
               fnGetMemoryValue = (iBuffer) => BitConverter.ToInt16(scanSearchBuffer, iBuffer);
               objectTypeSize = sizeof(Int16);
            }
            break;
            case TypeCode.UInt32: {
               searchValue = BitConverter.ToUInt32(scanValueBuffer, 0);
               fnGetMemoryValue = (iBuffer) => BitConverter.ToUInt32(scanSearchBuffer, iBuffer);
               objectTypeSize = sizeof(UInt32);
            }
            break;
            case TypeCode.Int32: {
               searchValue = BitConverter.ToInt32(scanValueBuffer, 0);
               fnGetMemoryValue = (iBuffer) => BitConverter.ToUInt32(scanSearchBuffer, iBuffer);
               objectTypeSize = sizeof(UInt32);
            }
            break;
            case TypeCode.UInt64: {
               searchValue = BitConverter.ToUInt64(scanValueBuffer, 0);
               fnGetMemoryValue = (iBuffer) => BitConverter.ToUInt64(scanSearchBuffer, iBuffer);
               objectTypeSize = sizeof(UInt32);
            }
            break;
            case TypeCode.Int64: {
               searchValue = BitConverter.ToInt64(scanValueBuffer, 0);
               fnGetMemoryValue = (iBuffer) => BitConverter.ToUInt64(scanSearchBuffer, iBuffer);
               objectTypeSize = sizeof(UInt64);
            }
            break;
            case TypeCode.String: {
               return scan(scanSearchBuffer, scanValueBuffer, typeof(Byte[]), scanCompareType);
            }
         }
         if (scanValueType == typeof(Byte[])) {
            Int32 indexEnd = scanSearchBuffer.Length - scanValueBuffer.Length;
            for (Int32 index = 0; index < indexEnd; index += scanValueBuffer.Length) {
               Boolean isFound = false;
               for (Int32 j = 0; j < scanValueBuffer.Length - 1; j++) {
                  isFound = scanSearchBuffer[index + j] == scanValueBuffer[j];
                  if (!isFound)
                     break;
               }
               if (isFound)
                  listResults.Add((UInt32)index);
            }
         } else {
            Int32 endOffset = scanSearchBuffer.Length - objectTypeSize;
            for (Int32 index = 0; index < endOffset; index += objectTypeSize) {
               if (Memory.CompareUtil.compare(searchValue, fnGetMemoryValue.Invoke(index), scanCompareType, extraParams))
                  listResults.Add((UInt32)index);

            }
         }
         return listResults;
      }
   }
}
