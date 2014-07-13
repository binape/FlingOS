﻿#region Copyright Notice
/// ------------------------------------------------------------------------------ ///
///                                                                                ///
///               All contents copyright � Edward Nutting 2014                     ///
///                                                                                ///
///        You may not share, reuse, redistribute or otherwise use the             ///
///        contents this file outside of the Fling OS project without              ///
///        the express permission of Edward Nutting or other copyright             ///
///        holder. Any changes (including but not limited to additions,            ///
///        edits or subtractions) made to or from this document are not            ///
///        your copyright. They are the copyright of the main copyright            ///
///        holder for all Fling OS files. At the time of writing, this             ///
///        owner was Edward Nutting. To be clear, owner(s) do not include          ///
///        developers, contributors or other project members.                      ///
///                                                                                ///
/// ------------------------------------------------------------------------------ ///
#endregion
    
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Reflection;
using Kernel.Debug.Data;

namespace Kernel.Compiler.Architectures.x86_32
{
    /// <summary>
    /// See base class documentation.
    /// </summary>
    public class Callvirt : ILOps.Callvirt
    {
        /// <summary>
        /// See base class documentation.
        /// </summary>
        /// <param name="anILOpInfo">See base class documentation.</param>
        /// <param name="aScannerState">See base class documentation.</param>
        /// <returns>See base class documentation.</returns>
        /// <exception cref="System.NotSupportedException">
        /// Thrown if any argument or the return value is a floating point number.
        /// </exception>
        public override string Convert(ILOpInfo anILOpInfo, ILScannerState aScannerState)
        {
            StringBuilder result = new StringBuilder();

            MethodBase methodToCall = anILOpInfo.MethodToCall;
            //The method to call is a method base
            //A method base can be either a method info i.e. a normal method
            //or a constructor method. The two types are treated separately.
            if(methodToCall is MethodInfo)
            {
                //Need to do callvirt related stuff to load address of method to call
                // - Get object ref from loaded args
                // - Get type table entry from object ref
                // - Get mehtod table from type table entry
                // - Scan method table for the method we want
                //      - If found, load method address
                // - Else, check for parent type method table
                //      - If no parent type method table, throw exception
                // - Else, scan parent type method table

                string methodIDValueWanted = aScannerState.GetMethodIDValue((MethodInfo)methodToCall);
                string loopTableEntries_Label = string.Format("{0}.IL_{1}_LoopMethodTable", 
                                                aScannerState.GetMethodID(aScannerState.CurrentILChunk.Method),
                                                anILOpInfo.Position);
                string call_Label = string.Format("{0}.IL_{1}_Call",
                                                aScannerState.GetMethodID(aScannerState.CurrentILChunk.Method),
                                                anILOpInfo.Position);
                string notEqual_Label = string.Format("{0}.IL_{1}_NotEqual",
                                                aScannerState.GetMethodID(aScannerState.CurrentILChunk.Method),
                                                anILOpInfo.Position);
                string endOfTable_Label = string.Format("{0}.IL_{1}_EndOfTable",
                                                aScannerState.GetMethodID(aScannerState.CurrentILChunk.Method),
                                                anILOpInfo.Position);
                string notFound_Label = string.Format("{0}.IL_{1}_NotFound",
                                                aScannerState.GetMethodID(aScannerState.CurrentILChunk.Method),
                                                anILOpInfo.Position);
                DB_Type declaringDBType = DebugDatabase.GetType(aScannerState.GetTypeID(methodToCall.DeclaringType));

                //Get object ref
                int bytesForAllParams = ((MethodInfo)methodToCall).GetParameters().Select(x => Utils.GetNumBytesForType(x.ParameterType)).Sum();
                result.AppendLine(string.Format("mov dword eax, [esp+{0}]", bytesForAllParams));
                
                //Get type ref
                int typeOffset = 0;
                #region Offset calculation
                {
                    //Get the child links of the type (i.e. the fields of the type)
                    List<DB_ComplexTypeLink> allChildLinks = declaringDBType.ChildTypes.OrderBy(x => x.ParentIndex).ToList();
                    //Get the DB type information for the field we want to load
                    DB_ComplexTypeLink theTypeLink = (from links in declaringDBType.ChildTypes
                                                      where links.FieldId == "_Type"
                                                      select links).First();
                    //Get all the fields that come before the field we want to load
                    //This is so we can calculate the offset (in memory, in bytes) from the start of the object
                    allChildLinks = allChildLinks.Where(x => x.ParentIndex < theTypeLink.ParentIndex).ToList();
                    //Calculate the offset
                    //We use StackBytesSize since fields that are reference types are only stored as a pointer
                    typeOffset = allChildLinks.Sum(x => x.ChildType.StackBytesSize);
                }
                #endregion
                result.AppendLine(string.Format("mov eax, [eax+{0}]", typeOffset));

                //Get method table ref
                int methodTablePtrOffset = 0;
                #region Offset calculation
                {
                    DB_Type typeDBType = DebugDatabase.GetType(aScannerState.GetTypeID(aScannerState.TypeClass));
                    //Get the child links of the type (i.e. the fields of the type)
                    List<DB_ComplexTypeLink> allChildLinks = typeDBType.ChildTypes.OrderBy(x => x.ParentIndex).ToList();
                    //Get the DB type information for the field we want to load
                    DB_ComplexTypeLink theTypeLink = (from links in typeDBType.ChildTypes
                                                      where links.FieldId == "MethodTablePtr"
                                                      select links).First();
                    //Get all the fields that come before the field we want to load
                    //This is so we can calculate the offset (in memory, in bytes) from the start of the object
                    allChildLinks = allChildLinks.Where(x => x.ParentIndex < theTypeLink.ParentIndex).ToList();
                    //Calculate the offset
                    //We use StackBytesSize since fields that are reference types are only stored as a pointer
                    methodTablePtrOffset = allChildLinks.Sum(x => x.ChildType.StackBytesSize);
                }
                #endregion
                result.AppendLine(string.Format("mov eax, [eax+{0}]", methodTablePtrOffset));

                //Loop through entries
                result.AppendLine(loopTableEntries_Label + ":");
                //Load ID Val for current entry
                result.AppendLine("mov ebx, [eax]");
                //Compare to wanted ID value
                result.AppendLine("cmp ebx, " + methodIDValueWanted);
                //If equal, load method address into eax
                result.AppendLine("jne " + notEqual_Label);
                result.AppendLine("mov eax, [eax+4]");
                result.AppendLine("jmp " + call_Label);
                result.AppendLine(notEqual_Label + ":");
                //Else, compare to 0 to check for end of table
                result.AppendLine("cmp ebx, 0");
                result.AppendLine("jz " + endOfTable_Label);
                //Not 0? Move to next entry then loop again
                result.AppendLine("add eax, 8");
                result.AppendLine("jmp " + loopTableEntries_Label);
                result.AppendLine(endOfTable_Label + ":");
                //Compare address value to 0
                //If not zero, there is a parent method table to check
                result.AppendLine("mov ebx, [eax+4]");
                result.AppendLine("cmp ebx, 0");
                result.AppendLine("jz " + notFound_Label);
                //Load parent method table and loop 
                result.AppendLine("mov eax, ebx");
                result.AppendLine("jmp " + loopTableEntries_Label);
                result.AppendLine(notFound_Label + ":");
                //Throw exception!
                result.AppendLine(string.Format("call {0}", aScannerState.GetMethodID(aScannerState.ThrowNullReferenceExceptionMethod)));

                result.AppendLine(call_Label + ":");

                //Allocate space on the stack for the return value as necessary
                Type retType = ((MethodInfo)methodToCall).ReturnType;
                StackItem returnItem = new StackItem()
                {
                    isFloat = Utils.IsFloat(retType),
                    sizeOnStackInBytes = Utils.GetNumBytesForType(retType)
                };
                //We do not push the return value onto the stack unless it has size > 0
                //We do not push the return value onto our stack at this point - it is pushed after the call is done

                if (returnItem.sizeOnStackInBytes != 0)
                {
                    if (returnItem.isFloat)
                    {
                        //SUPPORT - floats
                        throw new NotSupportedException("Cannot handle float return values!");
                    }
                    else if (returnItem.sizeOnStackInBytes == 4)
                    {
                        result.AppendLine("push dword 0");
                    }
                    else if (returnItem.sizeOnStackInBytes == 8)
                    {
                        result.AppendLine("push dword 0");
                        result.AppendLine("push dword 0");
                    }
                    else
                    {
                        throw new NotSupportedException("Invalid return stack operand size!");
                    }
                }


                //Append the actual call
                result.AppendLine("call eax");



                //After a call, we need to remove the return value and parameters from the stack
                //This is most easily done by just adding the total number of bytes for params and
                //return value to the stack pointer (ESP register).
                
                //Stores the number of bytes to add
                int bytesToAdd = 0;
                //All the parameters for the method that was called
                List<Type> allParams = ((MethodInfo)methodToCall).GetParameters().Select(x => x.ParameterType).ToList();
                //Go through each one
                if (!methodToCall.IsStatic)
                {
                    allParams.Insert(0, methodToCall.DeclaringType);
                }
                foreach (Type aParam in allParams)
                {
                    //Pop the paramter off our stack 
                    //(Note: Return value was never pushed onto our stack. See above)
                    aScannerState.CurrentStackFrame.Stack.Pop();
                    //Add the size of the paramter to the total number of bytes to pop
                    bytesToAdd += Utils.GetNumBytesForType(aParam);
                }
                //If the number of bytes to add to skip over params is > 0
                if (bytesToAdd > 0)
                {
                    //If there is a return value on the stack
                    if (returnItem.sizeOnStackInBytes != 0)
                    {
                        //We need to store the return value then pop all the params

                        //We now push the return value onto our stack as,
                        //after all is said and done below, it will be the 
                        //top item on the stack
                        aScannerState.CurrentStackFrame.Stack.Push(returnItem);

                        //SUPPORT - floats (with above)

                        //Pop the return value into the eax register
                        //We will push it back on after params are skipped over.
                        if (returnItem.sizeOnStackInBytes == 4)
                        {
                            result.AppendLine("pop dword eax");
                        }
                        else if (returnItem.sizeOnStackInBytes == 8)
                        {
                            result.AppendLine("pop dword eax");
                            result.AppendLine("pop dword edx");
                        }
                    }   
                    //Skip over the params
                    result.AppendLine(string.Format("add esp, {0}", bytesToAdd));
                    //If necessary, push the return value onto the stack.
                    if (returnItem.sizeOnStackInBytes != 0)
                    {
                        //SUPPORT - floats (with above)

                        //The return value was stored in eax
                        //So push it back onto the stack
                        if (returnItem.sizeOnStackInBytes == 4)
                        {
                            result.AppendLine("push dword eax");
                        }
                        else if (returnItem.sizeOnStackInBytes == 8)
                        {
                            result.AppendLine("push dword edx");
                            result.AppendLine("push dword eax");
                        }
                    }
                }
                //No params to skip over but we might still need to store return value
                else if (returnItem.sizeOnStackInBytes != 0)
                {
                    //The return value will be the top item on the stack.
                    //So all we need to do is push the return item onto our stack.
                    aScannerState.CurrentStackFrame.Stack.Push(returnItem);
                }
            }
            else if(methodToCall is ConstructorInfo)
            {
                throw new NotSupportedException("How the hell are we getting callvirts to constructor methods?!");
            }
            
            return result.ToString().Trim();
        }
    }
}
