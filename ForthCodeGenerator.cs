/*
 * Delta Forth .NET - World's first Forth compiler for the .NET platform
 * Copyright (C)1997-2002 Valer BOCAN, Romania (vbocan@dataman.ro, http://www.dataman.ro)
 * 
 * This program and its source code is distributed in the hope that it will
 * be useful. No warranty of any kind is provided.
 * Please DO NOT distribute modified copies of the source code.
 * 
 * If you like this software, please make a donation to a charity of your choice.
 */

using System;
using System.Collections;
using System.Reflection;
using System.Reflection.Emit;
using System.Threading;

namespace DeltaForth
{

	// IF structure
	// Definition of an IF structure used to code IF-ELSE-THEN
	struct _tagIF 
	{
		public Label lbElse;	// Label for the ELSE branch
		public bool bElse;		// TRUE if lbElse has already been used
		public Label lbEnd;		// Label for the end of the control struct
	}

	// BEGIN structure
	// Definition of a structure used to code BEGIN-AGAIN, BEGIN-UNTIL, BEGIN-WHILE-REPEAT
	struct _tagBEGIN
	{
		public Label lbBegin;	// Label for BEGIN
		public Label lbEnd;		// Label for REPEAT
	}

	// DO structure
	// Definition of a structure used to code DO-LOOP/+LOOP structure
	struct _tagDO
	{
		public Label lbDo;		// Label for DO
		public Label lbLoop;	// Label for LOOP
	}

	/// <summary>
	/// Delta Forth - The .NET Forth Compiler
	/// (C) Valer BOCAN (vbocan@dataman.ro)
	/// 
	/// Class ForthCodeGenerator
	/// 
	/// Date of creation:		Wednesday,	September 12, 2001
	/// Date of last update:	Tuesday,	January   15, 2002
	/// 
	/// Description:
	/// </summary>
	/// 
	public class ForthCodeGenerator
	{
		private AppDomain appDomain;			// Domain where we define the assemblies
		private AssemblyName assemblyName;		// AssemblyName
		private AssemblyBuilder assembly;		// Assembly
		private ModuleBuilder module;			// Module builder
		private TypeBuilder ForthEngineClass;	// DeltaForthEngine type

		private ArrayList Methods;				// Metods defined in the class
		private string LibraryName;				// Name of the library to be created
		private ArrayList GlobalConstants;		// List of global constants
		private ArrayList GlobalVariables;		// List of global variables
		private ArrayList LocalVariables;		// List of local variables
		private ArrayList Words;				// List of Forth words
		private ArrayList ExternalWords;		// List of external Forth words
		private MethodInfo StartupCode;			// Describes the pre-MAIN startup code
		private string TargetFileName;			// Name of the target file
		private bool bExe;						// TRUE if generating code for EXE, FALSE for DLL
		private bool bCheckStack;				// TRUE if generating exception catching code for each method
		private bool bExtCallerDefined;			// TRUE if the runtime contains the ExternalCaller function
												// The function is generated in the runtime area at the first external call
		private MethodBuilder extCaller;		// ExternalCaller method builder


		private int ForthStackOrigin;			// The Forth stack origin
		private FieldBuilder ForthStack;		// The Forth stack
		private FieldBuilder ForthStackIndex;	// The Forth stack index
		private int ReturnStackOrigin;			// The return stack origin
		private FieldBuilder ReturnStack;		// The return stack
		private FieldBuilder ReturnStackIndex;	// The return stack index

		private int ForthStackSize;		// The Forth stack size
		private int ReturnStackSize;	// The Return stack size
		private int Tib;				// TIB area offset
		private int Pad;				// PAD area offset
		private int LocalVarArea;		// A maximum of 1024 local variables can be defined

		// Temporary variables
		private FieldBuilder Dummy1;			// Dummy variable, used for temporary storage
		private FieldBuilder Dummy2;			// Dummy variable, used for temporary storage
		private FieldBuilder Dummy3;			// Dummy variable, used for temporary storage
		private FieldBuilder strDummy;			// Dummy variable, used for temporary storage
		private FieldBuilder DoLoopDummy;		// Dummy variable, used for Do-LOOP/+LOOP structure
			
		// Write methods (used when generating code that displays text)
		private MethodInfo WriteStringMethod;		// Describes the Write(string) method
		private MethodInfo WriteLineStringMethod;	// Describes the WriteLine(string) method
		private MethodInfo WriteIntMethod;			// Describes the Write(int) method
		private MethodInfo WriteCharMethod;			// Describes the Write(char) method

		// Control structures stacks
		private Stack IfStack;				// Stack for the IF-ELSE-THEN control structure
		private Stack BeginStack;			// Stack for the BEGIN-UNTIL control structure
		private Stack CaseStack;			// Stack for the CASE-ENDCASE control structure
		private Stack DoStack;				// Stack for the DO-LOOP/+LOOP control structure
		
		public ForthCodeGenerator(string p_TargetFileName, string p_TargetDirectory, string p_LibraryName, ArrayList p_GlobalConstants, ArrayList p_GlobalVariables, ArrayList p_LocalVariables, ArrayList p_Words, ArrayList p_ExternalWords, bool p_bExe, bool p_bCheckStack, int iForthStackSize, int iReturnStackSize)
		{
			// Initialize variables
			TargetFileName = p_TargetFileName;
			LibraryName = (p_LibraryName != null) ? p_LibraryName : "DeltaForthEngine";
			GlobalConstants = p_GlobalConstants;
			GlobalVariables = p_GlobalVariables;
			LocalVariables = p_LocalVariables;
			Words = p_Words;
			ExternalWords = p_ExternalWords;
			bExe = p_bExe;
			bCheckStack = p_bCheckStack;
			bExtCallerDefined = false;
			// Set stack sizes
			ForthStackSize = iForthStackSize;
			ReturnStackSize = iReturnStackSize;
			// Set system variables
			Tib = ForthStackSize - 64 - 80;
			Pad = ForthStackSize - 64;
			LocalVarArea = ForthStackSize - 64 - 80 - 1024; 

			// Initialize stack origins
			ForthStackOrigin = 0;
			ReturnStackOrigin = 0;
			
			// Initialize Write methods
			WriteStringMethod = typeof(Console).GetMethod("Write", new Type[] { typeof(String) });
			WriteLineStringMethod = typeof(Console).GetMethod("WriteLine", new Type[] { typeof(String) });
			WriteIntMethod = typeof(Console).GetMethod("Write", new Type[] { typeof(int) });
			WriteCharMethod = typeof(Console).GetMethod("Write", new Type[] { typeof(char) });

			// Initialize stacks
			IfStack = new Stack();
			BeginStack = new Stack();
			CaseStack = new Stack();
			DoStack = new Stack();

			// Initialize address of global variables
			for(int i = 0; i < GlobalVariables.Count; i++)
			{
				ForthVariable fv = (ForthVariable)GlobalVariables[i];
				fv.Address = ForthStackOrigin;
				ForthStackOrigin += fv.Size;		// Advance the stack origin to accomodate the variable size
				GlobalVariables[i] = fv;
			}

			Methods = new ArrayList();
			// ...

			appDomain = Thread.GetDomain();				// Initialize domain

			assemblyName = new AssemblyName();			// Create an assembly name
			assemblyName.Name = "DeltaForthEngine";

			// Create the assembly
			assembly = appDomain.DefineDynamicAssembly(assemblyName, AssemblyBuilderAccess.Save, p_TargetDirectory);

			// Create a module within the assembly
			module = assembly.DefineDynamicModule("DeltaForthModule", TargetFileName);

			// Define a public class
			ForthEngineClass = module.DefineType(LibraryName, TypeAttributes.Public | TypeAttributes.BeforeFieldInit);

			// Create the class constructor
			Type[] constructorArgs = {};
			ConstructorBuilder constructor = ForthEngineClass.DefineConstructor(MethodAttributes.Public, CallingConventions.Standard, constructorArgs);

			// Create the method "InitForthEngine" to initialize the Forth environment
			StartupCode = CreateStartupCode();

			// Generate IL code for the constructor
			ILGenerator constructorIL = constructor.GetILGenerator();
			constructorIL.Emit(OpCodes.Ldarg_0);
			ConstructorInfo superConstructor = typeof(Object).GetConstructor(new Type[0]);
			constructorIL.Emit(OpCodes.Call, superConstructor);
			constructorIL.Emit(OpCodes.Ret);
		}

		// CreateStartupCode - Creates a function that initializes the Forth stack, the return stack, system variables, etc.
		// Input:  None
		// Output: A MethodBuilder structure describing the pre-MAIN start-up code
		private MethodBuilder CreateStartupCode()
		{
			// Initialize the Forth stack
			ForthStack = ForthEngineClass.DefineField("ForthStack", typeof(int[]), FieldAttributes.Public|FieldAttributes.Static);
			ForthStackIndex = ForthEngineClass.DefineField("ForthStackIndex", typeof(int), FieldAttributes.Public|FieldAttributes.Static);
			// Initialize the return stack
			ReturnStack = ForthEngineClass.DefineField("ReturnStack", typeof(int[]), FieldAttributes.Private|FieldAttributes.Static);
			ReturnStackIndex = ForthEngineClass.DefineField("ReturnStackIndex", typeof(int), FieldAttributes.Private|FieldAttributes.Static);
			// Initialize dummy fields
			Dummy1 = ForthEngineClass.DefineField("dummy1", typeof(int), FieldAttributes.Private|FieldAttributes.Static);
			Dummy2 = ForthEngineClass.DefineField("dummy2", typeof(int), FieldAttributes.Private|FieldAttributes.Static);
			Dummy3 = ForthEngineClass.DefineField("dummy3", typeof(int), FieldAttributes.Private|FieldAttributes.Static);
			strDummy = ForthEngineClass.DefineField("strdummy", typeof(string), FieldAttributes.Private|FieldAttributes.Static);
			DoLoopDummy = ForthEngineClass.DefineField("dummy4", typeof(int), FieldAttributes.Private|FieldAttributes.Static);
			// Create the InitEngine method
			MethodBuilder InitMethod = ForthEngineClass.DefineMethod("InitEngine", MethodAttributes.Private | MethodAttributes.Static, typeof(void), null);
			ILGenerator ilgen = InitMethod.GetILGenerator();
			// Initialize ForthStack array
			ilgen.Emit(OpCodes.Ldc_I4, ForthStackSize);
			ilgen.Emit(OpCodes.Newarr, typeof(int));	// Was "int[]". Thanks to Brad Merrill from Microsoft.
			ilgen.Emit(OpCodes.Stsfld, ForthStack);
			// Initialize ReturnStack array
			ilgen.Emit(OpCodes.Ldc_I4, ReturnStackSize);
			ilgen.Emit(OpCodes.Newarr, typeof(int));	// Was "int[]". Thanks to Brad Merrill from Microsoft.
			ilgen.Emit(OpCodes.Stsfld, ReturnStack);
			// Initialize ForthStackIndex to origin
			ilgen.Emit(OpCodes.Ldc_I4, ForthStackOrigin);
			ilgen.Emit(OpCodes.Stsfld, ForthStackIndex);
			// Normal return from function
			ilgen.Emit(OpCodes.Ret);

			return InitMethod;
		}
		// SetEntryPoint - Sets the starting point of the program
		// Input:  The name of the method that starts the program
		// Output: None
		private void SetEntryPoint(string MethodName)
		{
			// Setup program entry point (MAIN code)
			MethodInfo entrypoint = GetMethod(MethodName).GetBaseDefinition();
			if(entrypoint == null)	// No MAIN function defined
			{
				entrypoint = StartupCode;	// Execution begins with the startup code
			}
			//if(bExe)
			//{
				assembly.SetEntryPoint(entrypoint, PEFileKinds.ConsoleApplication);
			//}
			//else
			//{
				// NOTE:
				// DF versions prior to 1.0 beta 2b used the line below to set up the entry point
				// of the DLL. With the advent of the .NET Framework RC, this technique didn't work
				// any more, since a DLL initialization exception occurs at runtime.
				//assembly.SetEntryPoint(entrypoint, PEFileKinds.Dll);
			//}
		}

		// AddMethod - Builds a method and adds it to the DeltaForthEngine class
		// Input:  MethodName - the name of the method
		// Output: A MethodBuilder structure
		private MethodBuilder AddMethod(string MethodName)
		{
			MethodBuilder ForthMethod = ForthEngineClass.DefineMethod(MethodName, MethodAttributes.Public | MethodAttributes.Static, typeof(void), null);
			Methods.Add(ForthMethod);	// Add method builder to our list
			return ForthMethod;
		}

		// GetMethod - Gets the information for a specified method
		// Input:  MethodName - the name of the method
		// Output: A MethodInformation structure
		private MethodBuilder GetMethod(string MethodName)
		{
			MethodBuilder ForthMethod;
			IEnumerator en = Methods.GetEnumerator();
			while(en.MoveNext())
			{
				ForthMethod = (MethodBuilder)en.Current;
				if(ForthMethod.Name.ToUpper() == MethodName.ToUpper())
					return ForthMethod;
			}
			return null;
		}

		// DoGenerateCode - Generates code out of available words
		// Input:  None
		// Output: None
		public void DoGenerateCode()
		{
			// Add all words to the module
			IEnumerator en = Words.GetEnumerator();
			while(en.MoveNext())
			{
				AddMethod(((ForthWord)en.Current).Name);
			}

			// Generate the code for each method (Forth word)
			en.Reset();
			while(en.MoveNext())
			{
				MethodBuilder mb = GetMethod(((ForthWord)en.Current).Name);
				GenerateMethodCode(mb);
			}

			// Save the assembly
			SetEntryPoint("MAIN");
			SaveAssembly();	
		}

		// SaveAssembly - Saves the generated code to target file
		// Input:  None
		// Output: None
		private void SaveAssembly()
		{
			try
			{
				ForthEngineClass.CreateType();
			}
			catch(System.InvalidOperationException)
			{
				throw new Exception("Could not write file " + TargetFileName);
			}
			assembly.Save(TargetFileName);
		}

		// GenerateMethodCode - Generate the code of the specified method
		// Input:  None
		// Output: None
		private void GenerateMethodCode(MethodBuilder mb)
		{
			string MethodName = mb.Name;
			ILGenerator MethodILGen = mb.GetILGenerator();

			// Initialize address of local variables for the current method
			for(int i = 0; i < LocalVariables.Count; i++)
			{
				ForthLocalVariable fv = (ForthLocalVariable)LocalVariables[i];
				if(fv.WordName == MethodName) 
				{
					fv.Address = LocalVarArea + i;
					LocalVariables[i] = fv;
				}
			}

			// If we are generating code for the MAIN function, we should call the startup code first
			if(MethodName == "MAIN") 
			{
				MethodILGen.Emit(OpCodes.Call, StartupCode);
			}

			// Each method should catch exceptions thrown by stack operations
			if(bCheckStack) MethodILGen.BeginExceptionBlock();

			// We have the method name, now look for the contents of the word
			ArrayList WordContents = null;
			IEnumerator en = Words.GetEnumerator();
			while(en.MoveNext())
			{
				ForthWord fw = (ForthWord)en.Current;
				if(fw.Name.ToUpper() == MethodName.ToUpper()) 
				{
					WordContents = fw.Definition;
					break;
				}
			}

			// The contents of the 'MethodName' word is now in the 'WordContents' list
			for(int i= 0; i < WordContents.Count; i++)
			{
				string atom = (string)WordContents[i];	// Get the current atom to be processed
				
				// Display statement ( ."<text>" )
				if(atom.StartsWith(".\"")) 
				{
					atom = atom.Remove(0, 2);						// Remove ."
					atom = atom.TrimEnd(new char[] {'\"'});			// Remove trailing "
					MethodILGen.Emit(OpCodes.Ldstr, atom);				// ldstr "text"
					MethodILGen.Emit(OpCodes.Call, WriteStringMethod);	// call WriteLine(string)
					continue;
				}

				// Dump statement ( "<text>")
				if(atom.StartsWith("\"")) 
				{
					atom = atom.Trim(new char[] {'\"'});			// Remove " from the beginning and the end of the string
					_Dump(MethodILGen, atom);				// Dump the string at the address specified on the stack
					continue;
				}

				switch(atom)
				{
					case "+":	// "+" - adds two numbers on the stack
						MathOp(MethodILGen, '+');
						break;
					case "-":	// "-" - adds two numbers on the stack
						MathOp(MethodILGen, '-');
						break;
					case "*":	// "*" - adds two numbers on the stack
						MathOp(MethodILGen, '*');
						break;
					case "/":	// "/" - adds two numbers on the stack
						MathOp(MethodILGen, '/');
						break;
					case "MOD":	// "MOD" - division remainder
						MathOp(MethodILGen, '%');
						break;
					case "/MOD": // "Slash MOD" - division remainder and result
						_SlashMod(MethodILGen);
						break;
					case "*/": // "Star Slash" - scaling operator
						_StarSlash(MethodILGen);
						break;
					case "*/MOD": // "Star Slash Mod" - scaling operator
						_StarSlashMod(MethodILGen);
						break;
					case "MINUS": // "Minus" - minus
						_Minus(MethodILGen);
						break;
					case "ABS":  // "Absolute" - absolute value
						_Abs(MethodILGen);
						break;
					case "MIN":	// "MIN" - minimum of two values
						_MinMax(MethodILGen, true);
						break;
					case "MAX":	// "MAX" - maximum of two values
						_MinMax(MethodILGen, false);
						break;
					case "1+":	// "One Plus" - adds 1 to the value on top of stack
						_OnePlus(MethodILGen);
						break;
					case "2+":	// "Two Plus" - adds 2 to the value on top of stack
						_TwoPlus(MethodILGen);
						break;
					case "0=":	// "Zero Equal" - test for equality with 0
						_ZeroEqual(MethodILGen);
						break;
					case "0<":	// "Zero Less" - test for 0 or less
						_ZeroLess(MethodILGen);
						break;
					case "=":	// "Equal" - test for equal
						_Equal(MethodILGen);
						break;
					case "<":	// "Less" - test for less
						_Less(MethodILGen);
						break;
					case ">":	// "Greater" - test for greater
						_Greater(MethodILGen);
						break;
					case "<>":	// "Not equal" - test for not-equal
						_NotEqual(MethodILGen);
						break;
					case "~AND":	// Bitwise AND
						_BitwiseOp(MethodILGen, '&');
						break;
					case "~OR":		// Bitwise OR
						_BitwiseOp(MethodILGen, '|');
						break;
					case "~XOR":	// Bitwise XOR
						_BitwiseOp(MethodILGen, '^');
						break;
					case "~NOT":	// Bitwise NOT
						_BitwiseNOT(MethodILGen);
						break;
					case "AND":		// Logical AND
						_LogicalAND(MethodILGen);
						break;
					case "OR":		// Logical OR
						_LogicalOR(MethodILGen);
						break;
					case "NOT":		// Logical NOT
						_LogicalNOT(MethodILGen);
						break;
					case "DUP":		// Duplicates the value on top of stack
						_Dup(MethodILGen);
						break;
					case "-DUP":	// Duplicates the value on top of stack unless it is 0
						_DashDup(MethodILGen);
						break;
					case "DROP":	// Removes the value on top of stack
						_Drop(MethodILGen);
						break;
					case "SWAP":	// Swaps the topmost two values on the stack
						_Swap(MethodILGen);
						break;
					case "OVER":	// Duplicates the second value on the stack
						_Over(MethodILGen);
						break;
					case "ROT":		// Rotates top three elements on the stack
						_Rot(MethodILGen);
						break;
					case "SP@":		// Stack pointer fetch
						_SPfetch(MethodILGen);
						break;
					case "RP@":		// Return stack pointer fetch
						_RPfetch(MethodILGen);
						break;
					case "SP!":		// Flush Forth stack
						_SPstore(MethodILGen);
						break;
					case "RP!":		// Flush return stack
						_RPstore(MethodILGen);
						break;
					case "@":		// Fetch
						_Fetch(MethodILGen);
						break;
					case "?":		// Question-mark
						_QuestionMark(MethodILGen);
						break;
					case "!":		// Store
						_Store(MethodILGen);
						break;
					case "+!":		// Plus store
						_PlusStore(MethodILGen);
						break;
					case "EMIT":	// Emit
						_Emit(MethodILGen);
						break;
					case ".":	
						MethodILGen.Emit(OpCodes.Ldsfld, ForthStack);			
						MethodILGen.Emit(OpCodes.Ldsfld, ForthStackIndex);	
						MethodILGen.Emit(OpCodes.Ldc_I4_1);
						MethodILGen.Emit(OpCodes.Sub);
						MethodILGen.Emit(OpCodes.Dup);
						MethodILGen.Emit(OpCodes.Stsfld, ForthStackIndex);
						MethodILGen.Emit(OpCodes.Ldelem_I4);
						MethodILGen.Emit(OpCodes.Call, WriteIntMethod);	// call WriteLine(int)
						break;
					case "CR":	// CR - moves the cursor to the beginning of the next line
						MethodILGen.Emit(OpCodes.Ldstr, "");				// ldstr ""
						MethodILGen.Emit(OpCodes.Call, WriteLineStringMethod);	// call WriteLine(string)
						break;
					case "SPACE":	// SPACE - displays an empty space on the screen
						MethodILGen.Emit(OpCodes.Ldstr, " ");				// ldstr " "
						MethodILGen.Emit(OpCodes.Call, WriteStringMethod);	// call WriteLine(string)
						break;
					case "SPACES":	// SPACES - displays a number of space on the screen
						_Spaces(MethodILGen);
						break;
					case "TYPE":	// TYPE - Types a text on the screen
						_Type(MethodILGen);
						break;
					case "PAD":		// PAD - 64-cell area
						_Pad(MethodILGen);
						break;
					case "TIB":		// TIB - 80-cell area
						_Tib(MethodILGen);
						break;
					case "S0":		// Forth stack origin
						_StackOrigin(MethodILGen, true);
						break;
					case "R0":		// Return stack origin
						_StackOrigin(MethodILGen, false);
						break;
					case "KEY":		// KEY - Places the key code on the stack
						_Key(MethodILGen);
						break;
					case "EXPECT":
						_Expect(MethodILGen);
						break;
					case "QUERY":
						_Query(MethodILGen);
						break;
					case ">R":
						_ToR(MethodILGen);
						break;
					case "R>":
						_RFrom(MethodILGen);
						break;
					case "I":
						_I(MethodILGen);
						break;
					case "FILL":
						_Fill(MethodILGen);
						break;
					case "ERASE":
						_Erase(MethodILGen, 0);
						break;
					case "BLANKS":
						_Erase(MethodILGen, 32);
						break;
					case "STR2INT":
						_Str2Int(MethodILGen);
						break;
					case "COUNT":
						_Count(MethodILGen);
						break;
					case "CMOVE":
						_CMove(MethodILGen);
						break;
					case "INT2STR":
						_Int2Str(MethodILGen);
						break;
					case "EXIT":
						_Exit(MethodILGen);
						break;
					case "IF":
						_If(MethodILGen);
						break;
					case "ELSE":
						_Else(MethodILGen);
						break;
					case "THEN":
						_Then(MethodILGen);
						break;
					case "BEGIN":
						_Begin(MethodILGen);
						break;
					case "UNTIL":
						_Until(MethodILGen);
						break;
					case "AGAIN":
						_Again(MethodILGen);
						break;
					case "WHILE":
						_While(MethodILGen);
						break;
					case "REPEAT":
						_Repeat(MethodILGen);
						break;
					case "CASE":
						_Case(MethodILGen);
						break;
					case "OF":
						_Of(MethodILGen);
						break;
					case "ENDOF":
						_EndOf(MethodILGen);
						break;
					case "ENDCASE":
						_EndCase(MethodILGen);
						break;
					case "DO":
						_Do(MethodILGen);
						break;
					case "LEAVE":
						_Leave(MethodILGen);
						break;
					case "LOOP":
						_Loop(MethodILGen);
						break;
					case "+LOOP":
						_PlusLoop(MethodILGen);
						break;
					default:
						try 
						{
							_PushStack(MethodILGen, Convert.ToInt32(atom, 10));		// Insert number
							break;
						}
						catch(Exception)
						{
							// It's not a number, see if it is something else
						}
						// Check whether the atom is a constant
						bool ConstFound = false;
						en = GlobalConstants.GetEnumerator();
						while(en.MoveNext() && !ConstFound) 
						{
							if(((ForthConstant)en.Current).Name.ToUpper() == atom) 
							{
								object val = ((ForthConstant)en.Current).Value;
								if(val.GetType() == typeof(int))
								{
									// The constant is of integer type
									_PushStack(MethodILGen, (int)val);
								} 
								else 
								{
									// The constant is of string type
									_Dump(MethodILGen, (string)val);
								}
								ConstFound = true;

							}
						}
						if(ConstFound) break;

						// Check whether the atom is a local variable
						bool LocalVarFound = false;
						en = LocalVariables.GetEnumerator();
						while(en.MoveNext() && !LocalVarFound) 
						{
							ForthLocalVariable flv = (ForthLocalVariable)en.Current;
							if((flv.Name.ToUpper() == atom) && (flv.WordName.ToUpper() == MethodName)) 
							{
								string varaddr = flv.Address.ToString();
								_PushStack(MethodILGen, Convert.ToInt32(varaddr, 10));
								LocalVarFound = true;
							}
						}
						if(LocalVarFound) break;

						// Check whether the atom is a variable
						bool VarFound = false;
						en = GlobalVariables.GetEnumerator();
						while(en.MoveNext() && !VarFound) 
						{
							if(((ForthVariable)en.Current).Name.ToUpper() == atom) 
							{
								string varaddr = ((ForthVariable)en.Current).Address.ToString();
								_PushStack(MethodILGen, Convert.ToInt32(varaddr, 10));
								VarFound = true;
							}
						}
						if(VarFound) break;

						// Check whether the atom is an external word
						bool ExternalWordFound = false;
						en = ExternalWords.GetEnumerator();
						while(en.MoveNext() && !ExternalWordFound) 
						{
							ExternalWord flv = (ExternalWord)en.Current;
							if((flv.Name.ToUpper() == atom)) 
							{
								CallExternalMethod(MethodILGen, flv.Library, flv.Class, flv.Method);
								ExternalWordFound = true;
							}
						}
						if(ExternalWordFound) break;

						// If it's not a known word, then raise and error
						MethodBuilder lmb = GetMethod(atom);
						if(lmb != null)
						{
							// It must be a word we're dealing with
							MethodILGen.Emit(OpCodes.Call, GetMethod(atom));	// Call function
						} 
						else
						{
							throw new Exception(atom + " in word " + MethodName + " is not known.");
						}
						break;
				}

			}
			// Catch the index out of bounds exception
			if(bCheckStack)
			{
				MethodILGen.BeginCatchBlock(typeof(IndexOutOfRangeException));
				MethodILGen.Emit(OpCodes.Pop);
				// If exception occured in MAIN, simply display an error message and return
				if(MethodName == "MAIN")
				{
					MethodILGen.EmitWriteLine("RUNTIME ERROR: Stack underflow or overflow.");
				}
				else
				{
					// If the exception occured in some method, rethrow the exception to MAIN
					MethodILGen.ThrowException(typeof(IndexOutOfRangeException));
				}

				// Catch the file not found exception (thrown by ExternalCaller)
				MethodILGen.BeginCatchBlock(typeof(System.IO.FileNotFoundException));
				MethodILGen.Emit(OpCodes.Pop);
				MethodILGen.EmitWriteLine("RUNTIME ERROR: Library file not found.");
				MethodILGen.EndExceptionBlock();
			}
			
			// Every function ends with the RET statement
			MethodILGen.Emit(OpCodes.Ret);
		}

		// MathOp - Generates code for operations +, -, *, /
		// 		ForthStack[ForthStackIndex - 2] = ForthStack[ForthStackIndex - 2] "MathOp" ForthStack[ForthStackIndex - 1];
		//		ForthStackIndex--;
		// Input:  ILGenerator for the method, operation to perform
		// Output: None
		private void MathOp(ILGenerator ilgen, char Op)
		{
			ilgen.Emit(OpCodes.Ldsfld, ForthStack);			
			ilgen.Emit(OpCodes.Ldsfld, ForthStackIndex);	
			ilgen.Emit(OpCodes.Ldc_I4_2);
			ilgen.Emit(OpCodes.Sub);

			ilgen.Emit(OpCodes.Ldsfld, ForthStack);			
			ilgen.Emit(OpCodes.Ldsfld, ForthStackIndex);	
			ilgen.Emit(OpCodes.Ldc_I4_2);
			ilgen.Emit(OpCodes.Sub);
			ilgen.Emit(OpCodes.Ldelem_I4);
			ilgen.Emit(OpCodes.Ldsfld, ForthStack);			
			ilgen.Emit(OpCodes.Ldsfld, ForthStackIndex);	
			ilgen.Emit(OpCodes.Ldc_I4_1);
			ilgen.Emit(OpCodes.Sub);
			ilgen.Emit(OpCodes.Ldelem_I4);
			// -------------------------
			switch(Op) 
			{
				case '+':
					ilgen.Emit(OpCodes.Add);
					break;
				case '-':
					ilgen.Emit(OpCodes.Sub);
					break;
				case '*':
					ilgen.Emit(OpCodes.Mul);
					break;
				case '/':
					ilgen.Emit(OpCodes.Div);
					break;
				case '%':
					ilgen.Emit(OpCodes.Rem);
					break;

			}
			// -------------------------
			ilgen.Emit(OpCodes.Stelem_I4);
			ilgen.Emit(OpCodes.Ldsfld, ForthStackIndex);	
			ilgen.Emit(OpCodes.Ldc_I4_1);
			ilgen.Emit(OpCodes.Sub);
			ilgen.Emit(OpCodes.Stsfld, ForthStackIndex);
		}

		// _PushStack - Push a value on the stack
		//		ForthStack[ForthStackIndex++] = "value";
		// Input:  ILGenerator for the method, value to push
		// Output: None
		private void _PushStack(ILGenerator ilgen, int value)
		{
			ilgen.Emit(OpCodes.Ldsfld, ForthStack);			
			ilgen.Emit(OpCodes.Ldsfld, ForthStackIndex);
			ilgen.Emit(OpCodes.Dup);
			ilgen.Emit(OpCodes.Ldc_I4_1);
			ilgen.Emit(OpCodes.Add);
			ilgen.Emit(OpCodes.Stsfld, ForthStackIndex);
			ilgen.Emit(OpCodes.Ldc_I4, value);
			ilgen.Emit(OpCodes.Stelem_I4);
		}

		// _SlashMod - Division remainder and result
		//		Dummy1 = ForthStack[ForthStackIndex - 1];
		//		Dummy2 = ForthStack[ForthStackIndex - 2];
		//		ForthStack[ForthStackIndex - 2] = Dummy2 % Dummy1;
		//		ForthStack[ForthStackIndex - 1] = Dummy2 / Dummy1;
		// Input:  ILGenerator for the method
		// Output: None
		private void _SlashMod(ILGenerator ilgen)
		{
			ilgen.Emit(OpCodes.Ldsfld, ForthStack);
			ilgen.Emit(OpCodes.Ldsfld, ForthStackIndex);
			ilgen.Emit(OpCodes.Ldc_I4_1);
			ilgen.Emit(OpCodes.Sub);
			ilgen.Emit(OpCodes.Ldelem_I4);
			ilgen.Emit(OpCodes.Stsfld, Dummy1);
			ilgen.Emit(OpCodes.Ldsfld, ForthStack);
			ilgen.Emit(OpCodes.Ldsfld, ForthStackIndex);
			ilgen.Emit(OpCodes.Ldc_I4_2);
			ilgen.Emit(OpCodes.Sub);
			ilgen.Emit(OpCodes.Ldelem_I4);
			ilgen.Emit(OpCodes.Stsfld, Dummy2);
			ilgen.Emit(OpCodes.Ldsfld, ForthStack);
			ilgen.Emit(OpCodes.Ldsfld, ForthStackIndex);
			ilgen.Emit(OpCodes.Ldc_I4_2);
			ilgen.Emit(OpCodes.Sub);
			ilgen.Emit(OpCodes.Ldsfld, Dummy2);
			ilgen.Emit(OpCodes.Ldsfld, Dummy1);
			ilgen.Emit(OpCodes.Rem);
			ilgen.Emit(OpCodes.Stelem_I4);
			ilgen.Emit(OpCodes.Ldsfld, ForthStack);
			ilgen.Emit(OpCodes.Ldsfld, ForthStackIndex);
			ilgen.Emit(OpCodes.Ldc_I4_1);
			ilgen.Emit(OpCodes.Sub);
			ilgen.Emit(OpCodes.Ldsfld, Dummy2);
			ilgen.Emit(OpCodes.Ldsfld, Dummy1);
			ilgen.Emit(OpCodes.Div);
			ilgen.Emit(OpCodes.Stelem_I4);
		}

		// _StarSlash - Scaling operator
		//		Dummy1 = ForthStack[ForthStackIndex - 1];
		//		Dummy2 = ForthStack[ForthStackIndex - 2];
		//		Dummy3 = ForthStack[ForthStackIndex - 3];
		//		ForthStackIndex-=2;
		//		ForthStack[ForthStackIndex - 1] = Dummy3 * Dummy2 / Dummy1;
		// Input:  ILGenerator for the method
		// Output: None
		private void _StarSlash(ILGenerator ilgen)
		{
			ilgen.Emit(OpCodes.Ldsfld, ForthStack);
			ilgen.Emit(OpCodes.Ldsfld, ForthStackIndex);
			ilgen.Emit(OpCodes.Ldc_I4_1);
			ilgen.Emit(OpCodes.Sub);
			ilgen.Emit(OpCodes.Ldelem_I4);
			ilgen.Emit(OpCodes.Stsfld, Dummy1);
			ilgen.Emit(OpCodes.Ldsfld, ForthStack);
			ilgen.Emit(OpCodes.Ldsfld, ForthStackIndex);
			ilgen.Emit(OpCodes.Ldc_I4_2);
			ilgen.Emit(OpCodes.Sub);
			ilgen.Emit(OpCodes.Ldelem_I4);
			ilgen.Emit(OpCodes.Stsfld, Dummy2);
			ilgen.Emit(OpCodes.Ldsfld, ForthStack);
			ilgen.Emit(OpCodes.Ldsfld, ForthStackIndex);
			ilgen.Emit(OpCodes.Ldc_I4_3);
			ilgen.Emit(OpCodes.Sub);
			ilgen.Emit(OpCodes.Ldelem_I4);
			ilgen.Emit(OpCodes.Stsfld, Dummy3);
			ilgen.Emit(OpCodes.Ldsfld, ForthStackIndex);
			ilgen.Emit(OpCodes.Ldc_I4_2);
			ilgen.Emit(OpCodes.Sub);
			ilgen.Emit(OpCodes.Stsfld, ForthStackIndex);
			ilgen.Emit(OpCodes.Ldsfld, ForthStack);
			ilgen.Emit(OpCodes.Ldsfld, ForthStackIndex);
			ilgen.Emit(OpCodes.Ldc_I4_1);
			ilgen.Emit(OpCodes.Sub);
			ilgen.Emit(OpCodes.Ldsfld, Dummy3);
			ilgen.Emit(OpCodes.Ldsfld, Dummy2);
			ilgen.Emit(OpCodes.Mul);
			ilgen.Emit(OpCodes.Ldsfld, Dummy1);
			ilgen.Emit(OpCodes.Div);
			ilgen.Emit(OpCodes.Stelem_I4);
		}

		// _StarSlashMod - Scaling operator
		//		Dummy1 = ForthStack[ForthStackIndex - 1];
		//		Dummy2 = ForthStack[ForthStackIndex - 2];
		//		Dummy3 = ForthStack[ForthStackIndex - 3];
		//		ForthStackIndex--;
		//		ForthStack[ForthStackIndex - 2] = (Dummy3 * Dummy2) % Dummy1;
		//		ForthStack[ForthStackIndex - 1] = Dummy3 * Dummy2 / Dummy1;
		// Input:  ILGenerator for the method
		// Output: None
		private void _StarSlashMod(ILGenerator ilgen)
		{
			ilgen.Emit(OpCodes.Ldsfld, ForthStack);
			ilgen.Emit(OpCodes.Ldsfld, ForthStackIndex);
			ilgen.Emit(OpCodes.Ldc_I4_1);
			ilgen.Emit(OpCodes.Sub);
			ilgen.Emit(OpCodes.Ldelem_I4);
			ilgen.Emit(OpCodes.Stsfld, Dummy1);
			ilgen.Emit(OpCodes.Ldsfld, ForthStack);
			ilgen.Emit(OpCodes.Ldsfld, ForthStackIndex);
			ilgen.Emit(OpCodes.Ldc_I4_2);
			ilgen.Emit(OpCodes.Sub);
			ilgen.Emit(OpCodes.Ldelem_I4);
			ilgen.Emit(OpCodes.Stsfld, Dummy2);
			ilgen.Emit(OpCodes.Ldsfld, ForthStack);
			ilgen.Emit(OpCodes.Ldsfld, ForthStackIndex);
			ilgen.Emit(OpCodes.Ldc_I4_3);
			ilgen.Emit(OpCodes.Sub);
			ilgen.Emit(OpCodes.Ldelem_I4);
			ilgen.Emit(OpCodes.Stsfld, Dummy3);
			ilgen.Emit(OpCodes.Ldsfld, ForthStackIndex);
			ilgen.Emit(OpCodes.Ldc_I4_1);
			ilgen.Emit(OpCodes.Sub);
			ilgen.Emit(OpCodes.Stsfld, ForthStackIndex);
			ilgen.Emit(OpCodes.Ldsfld, ForthStack);
			ilgen.Emit(OpCodes.Ldsfld, ForthStackIndex);
			ilgen.Emit(OpCodes.Ldc_I4_2);
			ilgen.Emit(OpCodes.Sub);
			ilgen.Emit(OpCodes.Ldsfld, Dummy3);
			ilgen.Emit(OpCodes.Ldsfld, Dummy2);
			ilgen.Emit(OpCodes.Mul);
			ilgen.Emit(OpCodes.Ldsfld, Dummy1);
			ilgen.Emit(OpCodes.Rem);
			ilgen.Emit(OpCodes.Stelem_I4);
			ilgen.Emit(OpCodes.Ldsfld, ForthStack);
			ilgen.Emit(OpCodes.Ldsfld, ForthStackIndex);
			ilgen.Emit(OpCodes.Ldc_I4_1);
			ilgen.Emit(OpCodes.Sub);
			ilgen.Emit(OpCodes.Ldsfld, Dummy3);
			ilgen.Emit(OpCodes.Ldsfld, Dummy2);
			ilgen.Emit(OpCodes.Mul);
			ilgen.Emit(OpCodes.Ldsfld, Dummy1);
			ilgen.Emit(OpCodes.Div);
			ilgen.Emit(OpCodes.Stelem_I4);
		}

		// _Minus - Changes the sign of the number on top of stack
		//		ForthStack[ForthStackIndex - 1] = - ForthStack[ForthStackIndex - 1];
		// Input:  ILGenerator for the method
		// Output: None
		private void _Minus(ILGenerator ilgen)
		{
			ilgen.Emit(OpCodes.Ldsfld, ForthStack);
			ilgen.Emit(OpCodes.Ldsfld, ForthStackIndex);
			ilgen.Emit(OpCodes.Ldc_I4_1);
			ilgen.Emit(OpCodes.Sub);
			ilgen.Emit(OpCodes.Ldsfld, ForthStack);
			ilgen.Emit(OpCodes.Ldsfld, ForthStackIndex);
			ilgen.Emit(OpCodes.Ldc_I4_1);
			ilgen.Emit(OpCodes.Sub);
			ilgen.Emit(OpCodes.Ldelem_I4);
			ilgen.Emit(OpCodes.Neg);
			ilgen.Emit(OpCodes.Stelem_I4);
		}

		// _Abs - Absolute value of number
		//
		// Input:  ILGenerator for the method
		//		ForthStack[ForthStackIndex - 1] = Math.Abs(ForthStack[ForthStackIndex - 1]);
		// Output: None
		private void _Abs(ILGenerator ilgen)
		{
			MethodInfo Abs = typeof(Math).GetMethod("Abs", new Type[] { typeof(int) });

			ilgen.Emit(OpCodes.Ldsfld, ForthStack);
			ilgen.Emit(OpCodes.Ldsfld, ForthStackIndex);
			ilgen.Emit(OpCodes.Ldc_I4_1);
			ilgen.Emit(OpCodes.Sub);
			ilgen.Emit(OpCodes.Ldsfld, ForthStack);
			ilgen.Emit(OpCodes.Ldsfld, ForthStackIndex);
			ilgen.Emit(OpCodes.Ldc_I4_1);
			ilgen.Emit(OpCodes.Sub);
			ilgen.Emit(OpCodes.Ldelem_I4);
			ilgen.Emit(OpCodes.Call, Abs);
			ilgen.Emit(OpCodes.Stelem_I4);
		}

		// _MinMax - Minimum and maximum value
		//
		// Input:  ILGenerator for the method
		//			Minimum - true if minimum is to calculated, false for maximum
		//		ForthStack[ForthStackIndex - 1] = Math.Min(ForthStack[ForthStackIndex - 1]);
		//		ForthStackIndex--;
		// Output: None
		private void _MinMax(ILGenerator ilgen, bool Minimum)
		{
			MethodInfo Min = typeof(Math).GetMethod("Min", new Type[] { typeof(int), typeof(int) });
			MethodInfo Max = typeof(Math).GetMethod("Max", new Type[] { typeof(int), typeof(int) });

			ilgen.Emit(OpCodes.Ldsfld, ForthStack);
			ilgen.Emit(OpCodes.Ldsfld, ForthStackIndex);
			ilgen.Emit(OpCodes.Ldc_I4_2);
			ilgen.Emit(OpCodes.Sub);
			ilgen.Emit(OpCodes.Ldsfld, ForthStack);
			ilgen.Emit(OpCodes.Ldsfld, ForthStackIndex);
			ilgen.Emit(OpCodes.Ldc_I4_1);
			ilgen.Emit(OpCodes.Sub);
			ilgen.Emit(OpCodes.Ldelem_I4);
			ilgen.Emit(OpCodes.Ldsfld, ForthStack);
			ilgen.Emit(OpCodes.Ldsfld, ForthStackIndex);
			ilgen.Emit(OpCodes.Ldc_I4_2);
			ilgen.Emit(OpCodes.Sub);
			ilgen.Emit(OpCodes.Ldelem_I4);
			if(Minimum)
				ilgen.Emit(OpCodes.Call, Min);
			else
				ilgen.Emit(OpCodes.Call, Max);
			ilgen.Emit(OpCodes.Stelem_I4);
			ilgen.Emit(OpCodes.Ldsfld, ForthStackIndex);
			ilgen.Emit(OpCodes.Ldc_I4_1);
			ilgen.Emit(OpCodes.Sub);
			ilgen.Emit(OpCodes.Stsfld, ForthStackIndex);
		}

		// _OnePlus - Adds 1 to the value on top of stack
		//
		// Input:  ILGenerator for the method
		//		ForthStack[ForthStackIndex - 1] = ForthStack[ForthStackIndex - 1] + 1;
		// Output: None
		private void _OnePlus(ILGenerator ilgen)
		{
			ilgen.Emit(OpCodes.Ldsfld, ForthStack);
			ilgen.Emit(OpCodes.Ldsfld, ForthStackIndex);
			ilgen.Emit(OpCodes.Ldc_I4_1);
			ilgen.Emit(OpCodes.Sub);
			ilgen.Emit(OpCodes.Ldsfld, ForthStack);
			ilgen.Emit(OpCodes.Ldsfld, ForthStackIndex);
			ilgen.Emit(OpCodes.Ldc_I4_1);
			ilgen.Emit(OpCodes.Sub);
			ilgen.Emit(OpCodes.Ldelem_I4);
			ilgen.Emit(OpCodes.Ldc_I4_1);
			ilgen.Emit(OpCodes.Add);
			ilgen.Emit(OpCodes.Stelem_I4);
		}

		// _TwoPlus - Adds 2 to the value on top of stack
		//
		// Input:  ILGenerator for the method
		//		ForthStack[ForthStackIndex - 1] = ForthStack[ForthStackIndex - 1] + 2;
		// Output: None
		private void _TwoPlus(ILGenerator ilgen)
		{
			ilgen.Emit(OpCodes.Ldsfld, ForthStack);
			ilgen.Emit(OpCodes.Ldsfld, ForthStackIndex);
			ilgen.Emit(OpCodes.Ldc_I4_1);
			ilgen.Emit(OpCodes.Sub);
			ilgen.Emit(OpCodes.Ldsfld, ForthStack);
			ilgen.Emit(OpCodes.Ldsfld, ForthStackIndex);
			ilgen.Emit(OpCodes.Ldc_I4_1);
			ilgen.Emit(OpCodes.Sub);
			ilgen.Emit(OpCodes.Ldelem_I4);
			ilgen.Emit(OpCodes.Ldc_I4_2);
			ilgen.Emit(OpCodes.Add);
			ilgen.Emit(OpCodes.Stelem_I4);
		}

		// _ZeroEqual - Test for "zero-equal"
		//
		// Input:  ILGenerator for the method
		//		ForthStack[ForthStackIndex - 1] = (ForthStack[ForthStackIndex - 1] == 0) ? 1 : 0;
		// Output: None
		private void _ZeroEqual(ILGenerator ilgen)
		{
			Label lbZero = ilgen.DefineLabel();
			Label lbOne = ilgen.DefineLabel();

			ilgen.Emit(OpCodes.Ldsfld, ForthStack);
			ilgen.Emit(OpCodes.Ldsfld, ForthStackIndex);
			ilgen.Emit(OpCodes.Ldc_I4_1);
			ilgen.Emit(OpCodes.Sub);

			ilgen.Emit(OpCodes.Ldsfld, ForthStack);
			ilgen.Emit(OpCodes.Ldsfld, ForthStackIndex);
			ilgen.Emit(OpCodes.Ldc_I4_1);
			ilgen.Emit(OpCodes.Sub);

			ilgen.Emit(OpCodes.Ldelem_I4);
			ilgen.Emit(OpCodes.Brfalse_S, lbOne);
			ilgen.Emit(OpCodes.Ldc_I4_0);
			ilgen.Emit(OpCodes.Br_S, lbZero);
			ilgen.MarkLabel(lbOne);
			ilgen.Emit(OpCodes.Ldc_I4_1);
			ilgen.MarkLabel(lbZero);
			ilgen.Emit(OpCodes.Stelem_I4);
		}

		// _ZeroLess - Test for "zero or less"
		// Input:  ILGenerator for the method
		//		ForthStack[ForthStackIndex - 1] = (ForthStack[ForthStackIndex - 1] <= 0) ? 1 : 0;
		// Output: None
		private void _ZeroLess(ILGenerator ilgen)
		{
			Label lbZero = ilgen.DefineLabel();
			Label lbOne = ilgen.DefineLabel();

			ilgen.Emit(OpCodes.Ldsfld, ForthStack);
			ilgen.Emit(OpCodes.Ldsfld, ForthStackIndex);
			ilgen.Emit(OpCodes.Ldc_I4_1);
			ilgen.Emit(OpCodes.Sub);
			ilgen.Emit(OpCodes.Ldsfld, ForthStack);
			ilgen.Emit(OpCodes.Ldsfld, ForthStackIndex);
			ilgen.Emit(OpCodes.Ldc_I4_1);
			ilgen.Emit(OpCodes.Sub);
			ilgen.Emit(OpCodes.Ldelem_I4);
			ilgen.Emit(OpCodes.Ldc_I4_0);
			ilgen.Emit(OpCodes.Ble_S, lbOne);
			ilgen.Emit(OpCodes.Ldc_I4_0);
			ilgen.Emit(OpCodes.Br_S, lbZero);
			ilgen.MarkLabel(lbOne);
			ilgen.Emit(OpCodes.Ldc_I4_1);
			ilgen.MarkLabel(lbZero);
			ilgen.Emit(OpCodes.Stelem_I4);
		}

		// _Equal - Test for "equal"
		// Input:  ILGenerator for the method
		//		ForthStack[ForthStackIndex - 2] = (ForthStack[ForthStackIndex - 1] == ForthStack[ForthStackIndex - 2]) ? 1 : 0;
		//		ForthStackIndex--;
		// Output: None
		private void _Equal(ILGenerator ilgen)
		{
			Label lb1 = ilgen.DefineLabel();
			Label lb2 = ilgen.DefineLabel();

			ilgen.Emit(OpCodes.Ldsfld, ForthStack);
			ilgen.Emit(OpCodes.Ldsfld, ForthStackIndex);
			ilgen.Emit(OpCodes.Ldc_I4_2);
			ilgen.Emit(OpCodes.Sub);
			ilgen.Emit(OpCodes.Ldsfld, ForthStack);
			ilgen.Emit(OpCodes.Ldsfld, ForthStackIndex);
			ilgen.Emit(OpCodes.Ldc_I4_1);
			ilgen.Emit(OpCodes.Sub);
			ilgen.Emit(OpCodes.Ldelem_I4);
			ilgen.Emit(OpCodes.Ldsfld, ForthStack);
			ilgen.Emit(OpCodes.Ldsfld, ForthStackIndex);
			ilgen.Emit(OpCodes.Ldc_I4_2);
			ilgen.Emit(OpCodes.Sub);
			ilgen.Emit(OpCodes.Ldelem_I4);
			ilgen.Emit(OpCodes.Beq_S, lb1);
			ilgen.Emit(OpCodes.Ldc_I4_0);
			ilgen.Emit(OpCodes.Br_S, lb2);
			ilgen.MarkLabel(lb1);
			ilgen.Emit(OpCodes.Ldc_I4_1);
			ilgen.MarkLabel(lb2);
			ilgen.Emit(OpCodes.Stelem_I4);
			ilgen.Emit(OpCodes.Ldsfld, ForthStackIndex);
			ilgen.Emit(OpCodes.Ldc_I4_1);
			ilgen.Emit(OpCodes.Sub);
			ilgen.Emit(OpCodes.Stsfld, ForthStackIndex);
		}

		// _Less - Test for "less"
		// Input:  ILGenerator for the method
		//		ForthStack[ForthStackIndex - 2] = (ForthStack[ForthStackIndex - 2] < ForthStack[ForthStackIndex - 1]) ? 1 : 0;
		//		ForthStackIndex--;
		// Output: None
		private void _Less(ILGenerator ilgen)
		{
			Label lb1 = ilgen.DefineLabel();
			Label lb2 = ilgen.DefineLabel();

			ilgen.Emit(OpCodes.Ldsfld, ForthStack);
			ilgen.Emit(OpCodes.Ldsfld, ForthStackIndex);
			ilgen.Emit(OpCodes.Ldc_I4_2);
			ilgen.Emit(OpCodes.Sub);
			ilgen.Emit(OpCodes.Ldsfld, ForthStack);
			ilgen.Emit(OpCodes.Ldsfld, ForthStackIndex);
			ilgen.Emit(OpCodes.Ldc_I4_2);
			ilgen.Emit(OpCodes.Sub);
			ilgen.Emit(OpCodes.Ldelem_I4);
			ilgen.Emit(OpCodes.Ldsfld, ForthStack);
			ilgen.Emit(OpCodes.Ldsfld, ForthStackIndex);
			ilgen.Emit(OpCodes.Ldc_I4_1);
			ilgen.Emit(OpCodes.Sub);
			ilgen.Emit(OpCodes.Ldelem_I4);
			ilgen.Emit(OpCodes.Blt_S, lb1);
			ilgen.Emit(OpCodes.Ldc_I4_0);
			ilgen.Emit(OpCodes.Br_S, lb2);
			ilgen.MarkLabel(lb1);
			ilgen.Emit(OpCodes.Ldc_I4_1);
			ilgen.MarkLabel(lb2);
			ilgen.Emit(OpCodes.Stelem_I4);
			ilgen.Emit(OpCodes.Ldsfld, ForthStackIndex);
			ilgen.Emit(OpCodes.Ldc_I4_1);
			ilgen.Emit(OpCodes.Sub);
			ilgen.Emit(OpCodes.Stsfld, ForthStackIndex);
		}

		// _Greater - Test for "greater"
		// Input:  ILGenerator for the method
		//		ForthStack[ForthStackIndex - 2] = (ForthStack[ForthStackIndex - 2] > ForthStack[ForthStackIndex - 1]) ? 1 : 0;
		//		ForthStackIndex--;
		// Output: None
		private void _Greater(ILGenerator ilgen)
		{
			Label lb1 = ilgen.DefineLabel();
			Label lb2 = ilgen.DefineLabel();

			ilgen.Emit(OpCodes.Ldsfld, ForthStack);
			ilgen.Emit(OpCodes.Ldsfld, ForthStackIndex);
			ilgen.Emit(OpCodes.Ldc_I4_2);
			ilgen.Emit(OpCodes.Sub);
			ilgen.Emit(OpCodes.Ldsfld, ForthStack);
			ilgen.Emit(OpCodes.Ldsfld, ForthStackIndex);
			ilgen.Emit(OpCodes.Ldc_I4_2);
			ilgen.Emit(OpCodes.Sub);
			ilgen.Emit(OpCodes.Ldelem_I4);
			ilgen.Emit(OpCodes.Ldsfld, ForthStack);
			ilgen.Emit(OpCodes.Ldsfld, ForthStackIndex);
			ilgen.Emit(OpCodes.Ldc_I4_1);
			ilgen.Emit(OpCodes.Sub);
			ilgen.Emit(OpCodes.Ldelem_I4);
			ilgen.Emit(OpCodes.Bgt_S, lb1);
			ilgen.Emit(OpCodes.Ldc_I4_0);
			ilgen.Emit(OpCodes.Br_S, lb2);
			ilgen.MarkLabel(lb1);
			ilgen.Emit(OpCodes.Ldc_I4_1);
			ilgen.MarkLabel(lb2);
			ilgen.Emit(OpCodes.Stelem_I4);
			ilgen.Emit(OpCodes.Ldsfld, ForthStackIndex);
			ilgen.Emit(OpCodes.Ldc_I4_1);
			ilgen.Emit(OpCodes.Sub);
			ilgen.Emit(OpCodes.Stsfld, ForthStackIndex);
		}

		// _NotEqual - Test for "not equal"
		// Input:  ILGenerator for the method
		//		ForthStack[ForthStackIndex - 2] = (ForthStack[ForthStackIndex - 2] <> ForthStack[ForthStackIndex - 1]) ? 1 : 0;
		//		ForthStackIndex--;
		// Output: None
		private void _NotEqual(ILGenerator ilgen)
		{
			Label lb1 = ilgen.DefineLabel();
			Label lb2 = ilgen.DefineLabel();

			ilgen.Emit(OpCodes.Ldsfld, ForthStack);
			ilgen.Emit(OpCodes.Ldsfld, ForthStackIndex);
			ilgen.Emit(OpCodes.Ldc_I4_2);
			ilgen.Emit(OpCodes.Sub);
			ilgen.Emit(OpCodes.Ldsfld, ForthStack);
			ilgen.Emit(OpCodes.Ldsfld, ForthStackIndex);
			ilgen.Emit(OpCodes.Ldc_I4_2);
			ilgen.Emit(OpCodes.Sub);
			ilgen.Emit(OpCodes.Ldelem_I4);
			ilgen.Emit(OpCodes.Ldsfld, ForthStack);
			ilgen.Emit(OpCodes.Ldsfld, ForthStackIndex);
			ilgen.Emit(OpCodes.Ldc_I4_1);
			ilgen.Emit(OpCodes.Sub);
			ilgen.Emit(OpCodes.Ldelem_I4);
			ilgen.Emit(OpCodes.Bne_Un_S, lb1);
			ilgen.Emit(OpCodes.Ldc_I4_0);
			ilgen.Emit(OpCodes.Br_S, lb2);
			ilgen.MarkLabel(lb1);
			ilgen.Emit(OpCodes.Ldc_I4_1);
			ilgen.MarkLabel(lb2);
			ilgen.Emit(OpCodes.Stelem_I4);
			ilgen.Emit(OpCodes.Ldsfld, ForthStackIndex);
			ilgen.Emit(OpCodes.Ldc_I4_1);
			ilgen.Emit(OpCodes.Sub);
			ilgen.Emit(OpCodes.Stsfld, ForthStackIndex);
		}

		// _BitwiseOp - Bitwise operations (AND, OR, XOR)
		// Input:  ILGenerator for the method
		//		ForthStack[ForthStackIndex - 2] = ForthStack[ForthStackIndex - 2] "op" ForthStack[ForthStackIndex - 1];
		//		ForthStackIndex--;
		// Output: None
		private void _BitwiseOp(ILGenerator ilgen, char Op)
		{
			ilgen.Emit(OpCodes.Ldsfld, ForthStack);
			ilgen.Emit(OpCodes.Ldsfld, ForthStackIndex);
			ilgen.Emit(OpCodes.Ldc_I4_2);
			ilgen.Emit(OpCodes.Sub);
			ilgen.Emit(OpCodes.Ldsfld, ForthStack);
			ilgen.Emit(OpCodes.Ldsfld, ForthStackIndex);
			ilgen.Emit(OpCodes.Ldc_I4_2);
			ilgen.Emit(OpCodes.Sub);
			ilgen.Emit(OpCodes.Ldelem_I4);
			ilgen.Emit(OpCodes.Ldsfld, ForthStack);
			ilgen.Emit(OpCodes.Ldsfld, ForthStackIndex);
			ilgen.Emit(OpCodes.Ldc_I4_1);
			ilgen.Emit(OpCodes.Sub);
			ilgen.Emit(OpCodes.Ldelem_I4);
			switch(Op)
			{
				case '&':
					ilgen.Emit(OpCodes.And);
					break;
				case '|':
					ilgen.Emit(OpCodes.Or);
					break;
				case '^':
					ilgen.Emit(OpCodes.Xor);
					break;
			}
			ilgen.Emit(OpCodes.Stelem_I4);
			ilgen.Emit(OpCodes.Ldsfld, ForthStackIndex);
			ilgen.Emit(OpCodes.Ldc_I4_1);
			ilgen.Emit(OpCodes.Sub);
			ilgen.Emit(OpCodes.Stsfld, ForthStackIndex);
		}

		// _BitwiseNOT - Bitwise NOT
		// Input:  ILGenerator for the method
		//		ForthStack[ForthStackIndex - 1] = ~ForthStack[ForthStackIndex - 1];
		// Output: None
		private void _BitwiseNOT(ILGenerator ilgen)
		{
			ilgen.Emit(OpCodes.Ldsfld, ForthStack);
			ilgen.Emit(OpCodes.Ldsfld, ForthStackIndex);
			ilgen.Emit(OpCodes.Ldc_I4_1);
			ilgen.Emit(OpCodes.Sub);
			ilgen.Emit(OpCodes.Ldsfld, ForthStack);
			ilgen.Emit(OpCodes.Ldsfld, ForthStackIndex);
			ilgen.Emit(OpCodes.Ldc_I4_1);
			ilgen.Emit(OpCodes.Sub);
			ilgen.Emit(OpCodes.Ldelem_I4);
			ilgen.Emit(OpCodes.Not);
			ilgen.Emit(OpCodes.Stelem_I4);
		}

		// _LogicalAND - Logical AND
		// Input:  ILGenerator for the method
		//		ForthStack[ForthStackIndex - 2] = ((ForthStack[ForthStackIndex - 2] > 0) && (ForthStack[ForthStackIndex - 1] > 0) ? 1 : 0);
		//		ForthStackIndex--;
		// Output: None
		private void _LogicalAND(ILGenerator ilgen)
		{
			Label lb1 = ilgen.DefineLabel();
			Label lb2 = ilgen.DefineLabel();
			Label lb3 = ilgen.DefineLabel();

			ilgen.Emit(OpCodes.Ldsfld, ForthStack);
			ilgen.Emit(OpCodes.Ldsfld, ForthStackIndex);
			ilgen.Emit(OpCodes.Ldc_I4_2);
			ilgen.Emit(OpCodes.Sub);
			ilgen.Emit(OpCodes.Ldsfld, ForthStack);
			ilgen.Emit(OpCodes.Ldsfld, ForthStackIndex);
			ilgen.Emit(OpCodes.Ldc_I4_2);
			ilgen.Emit(OpCodes.Sub);
			ilgen.Emit(OpCodes.Ldelem_I4);	
			ilgen.Emit(OpCodes.Ldc_I4_0);
			ilgen.Emit(OpCodes.Ble_S, lb1);
			ilgen.Emit(OpCodes.Ldsfld, ForthStack);
			ilgen.Emit(OpCodes.Ldsfld, ForthStackIndex);
			ilgen.Emit(OpCodes.Ldc_I4_1);
			ilgen.Emit(OpCodes.Sub);
			ilgen.Emit(OpCodes.Ldelem_I4);
			ilgen.Emit(OpCodes.Ldc_I4_0);
			ilgen.Emit(OpCodes.Bgt_S, lb2);
			ilgen.MarkLabel(lb1);
			ilgen.Emit(OpCodes.Ldc_I4_0);
			ilgen.Emit(OpCodes.Br_S, lb3);
			ilgen.MarkLabel(lb2);
			ilgen.Emit(OpCodes.Ldc_I4_1);
			ilgen.MarkLabel(lb3);
			ilgen.Emit(OpCodes.Stelem_I4);
			ilgen.Emit(OpCodes.Ldsfld, ForthStackIndex);
			ilgen.Emit(OpCodes.Ldc_I4_1);
			ilgen.Emit(OpCodes.Sub);
			ilgen.Emit(OpCodes.Stsfld, ForthStackIndex);
		}

		// _LogicalOR - Logical OR
		// Input:  ILGenerator for the method
		//		ForthStack[ForthStackIndex - 2] = ((ForthStack[ForthStackIndex - 2] > 0) || (ForthStack[ForthStackIndex - 1] > 0) ? 1 : 0);
		//		ForthStackIndex--;
		// Output: None
		private void _LogicalOR(ILGenerator ilgen)
		{
			Label lb1 = ilgen.DefineLabel();
			Label lb2 = ilgen.DefineLabel();
			Label lb3 = ilgen.DefineLabel();

			ilgen.Emit(OpCodes.Ldsfld, ForthStack);
			ilgen.Emit(OpCodes.Ldsfld, ForthStackIndex);
			ilgen.Emit(OpCodes.Ldc_I4_2);
			ilgen.Emit(OpCodes.Sub);
			ilgen.Emit(OpCodes.Ldsfld, ForthStack);
			ilgen.Emit(OpCodes.Ldsfld, ForthStackIndex);
			ilgen.Emit(OpCodes.Ldc_I4_2);
			ilgen.Emit(OpCodes.Sub);
			ilgen.Emit(OpCodes.Ldelem_I4);	
			ilgen.Emit(OpCodes.Ldc_I4_0);
			ilgen.Emit(OpCodes.Bgt_S, lb1);
			ilgen.Emit(OpCodes.Ldsfld, ForthStack);
			ilgen.Emit(OpCodes.Ldsfld, ForthStackIndex);
			ilgen.Emit(OpCodes.Ldc_I4_1);
			ilgen.Emit(OpCodes.Sub);
			ilgen.Emit(OpCodes.Ldelem_I4);
			ilgen.Emit(OpCodes.Ldc_I4_0);
			ilgen.Emit(OpCodes.Bgt_S, lb1);
			ilgen.Emit(OpCodes.Ldc_I4_0);
			ilgen.Emit(OpCodes.Br_S, lb2);
			ilgen.MarkLabel(lb1);
			ilgen.Emit(OpCodes.Ldc_I4_1);
			ilgen.MarkLabel(lb2);
			ilgen.Emit(OpCodes.Stelem_I4);
			ilgen.Emit(OpCodes.Ldsfld, ForthStackIndex);
			ilgen.Emit(OpCodes.Ldc_I4_1);
			ilgen.Emit(OpCodes.Sub);
			ilgen.Emit(OpCodes.Stsfld, ForthStackIndex);
		}

		// _LogicalNOT - Logical NOT
		// Input:  ILGenerator for the method
		//		ForthStack[ForthStackIndex - 1] = (ForthStack[ForthStackIndex - 1] != 0) ? 0 : 1;
		// Output: None
		private void _LogicalNOT(ILGenerator ilgen)
		{
			Label lb1 = ilgen.DefineLabel();
			Label lb2 = ilgen.DefineLabel();
			
			ilgen.Emit(OpCodes.Ldsfld, ForthStack);
			ilgen.Emit(OpCodes.Ldsfld, ForthStackIndex);
			ilgen.Emit(OpCodes.Ldc_I4_1);
			ilgen.Emit(OpCodes.Sub);
			ilgen.Emit(OpCodes.Ldsfld, ForthStack);
			ilgen.Emit(OpCodes.Ldsfld, ForthStackIndex);
			ilgen.Emit(OpCodes.Ldc_I4_1);
			ilgen.Emit(OpCodes.Sub);
			ilgen.Emit(OpCodes.Ldelem_I4);
			ilgen.Emit(OpCodes.Brtrue_S, lb1);
			ilgen.Emit(OpCodes.Ldc_I4_1);
			ilgen.Emit(OpCodes.Br_S, lb2);
			ilgen.MarkLabel(lb1);
			ilgen.Emit(OpCodes.Ldc_I4_0);
			ilgen.MarkLabel(lb2);
			ilgen.Emit(OpCodes.Stelem_I4);
		}

		// _Dup - Duplicates the element on top of stack
		// Input:  ILGenerator for the method
		//		ForthStack[ForthStackIndex] = ForthStack[ForthStackIndex - 1];
		//		ForthStackIndex++;
		// Output: None
		private void _Dup(ILGenerator ilgen)
		{
			ilgen.Emit(OpCodes.Ldsfld, ForthStack);
			ilgen.Emit(OpCodes.Ldsfld, ForthStackIndex);
			ilgen.Emit(OpCodes.Ldsfld, ForthStack);
			ilgen.Emit(OpCodes.Ldsfld, ForthStackIndex);
			ilgen.Emit(OpCodes.Ldc_I4_1);
			ilgen.Emit(OpCodes.Sub);
			ilgen.Emit(OpCodes.Ldelem_I4);
			ilgen.Emit(OpCodes.Stelem_I4);
			ilgen.Emit(OpCodes.Ldsfld, ForthStackIndex);
			ilgen.Emit(OpCodes.Ldc_I4_1);
			ilgen.Emit(OpCodes.Add);
			ilgen.Emit(OpCodes.Stsfld, ForthStackIndex);
		}

		// _DashDup - Duplicates the element on top of stack unless it is 0
		// Input:  ILGenerator for the method
		//		if(ForthStack[ForthStackIndex - 1] != 0) ForthStack[ForthStackIndex] = ForthStack[ForthStackIndex - 1];
		//		ForthStackIndex++;
		// Output: None
		private void _DashDup(ILGenerator ilgen)
		{
			Label lb = ilgen.DefineLabel();

			ilgen.Emit(OpCodes.Ldsfld, ForthStack);
			ilgen.Emit(OpCodes.Ldsfld, ForthStackIndex);
			ilgen.Emit(OpCodes.Ldc_I4_1);
			ilgen.Emit(OpCodes.Sub);
			ilgen.Emit(OpCodes.Ldelem_I4);
			ilgen.Emit(OpCodes.Brfalse_S, lb);
			ilgen.Emit(OpCodes.Ldsfld, ForthStack);
			ilgen.Emit(OpCodes.Ldsfld, ForthStackIndex);
			ilgen.Emit(OpCodes.Ldsfld, ForthStack);
			ilgen.Emit(OpCodes.Ldsfld, ForthStackIndex);
			ilgen.Emit(OpCodes.Ldc_I4_1);
			ilgen.Emit(OpCodes.Sub);
			ilgen.Emit(OpCodes.Ldelem_I4);
			ilgen.Emit(OpCodes.Stelem_I4);
			ilgen.Emit(OpCodes.Ldsfld, ForthStackIndex);
			ilgen.Emit(OpCodes.Ldc_I4_1);
			ilgen.Emit(OpCodes.Add);
			ilgen.Emit(OpCodes.Stsfld, ForthStackIndex);
			ilgen.MarkLabel(lb);
			ilgen.Emit(OpCodes.Nop);
		}

		// _Drop - Removes the element on top of stack
		// Input:  ILGenerator for the method
		//		ForthStackIndex--;
		// Output: None
		private void _Drop(ILGenerator ilgen)
		{
			ilgen.Emit(OpCodes.Ldsfld, ForthStackIndex);
			ilgen.Emit(OpCodes.Ldc_I4_1);
			ilgen.Emit(OpCodes.Sub);
			ilgen.Emit(OpCodes.Stsfld, ForthStackIndex);
		}

		// _Swap - Swaps the two elements on the top of stack
		// Input:  ILGenerator for the method
		//		Dummy1 = ForthStack[ForthStackIndex - 1];
		//		ForthStack[ForthStackIndex - 1] = ForthStack[ForthStackIndex - 2];
		//		ForthStack[ForthStackIndex - 2] = Dummy1;
		// Output: None
		private void _Swap(ILGenerator ilgen)
		{
			ilgen.Emit(OpCodes.Ldsfld, ForthStack);
			ilgen.Emit(OpCodes.Ldsfld, ForthStackIndex);
			ilgen.Emit(OpCodes.Ldc_I4_1);
			ilgen.Emit(OpCodes.Sub);
			ilgen.Emit(OpCodes.Ldelem_I4);
			ilgen.Emit(OpCodes.Stsfld, Dummy1);
			ilgen.Emit(OpCodes.Ldsfld, ForthStack);
			ilgen.Emit(OpCodes.Ldsfld, ForthStackIndex);
			ilgen.Emit(OpCodes.Ldc_I4_1);
			ilgen.Emit(OpCodes.Sub);
			ilgen.Emit(OpCodes.Ldsfld, ForthStack);
			ilgen.Emit(OpCodes.Ldsfld, ForthStackIndex);
			ilgen.Emit(OpCodes.Ldc_I4_2);
			ilgen.Emit(OpCodes.Sub);
			ilgen.Emit(OpCodes.Ldelem_I4);
			ilgen.Emit(OpCodes.Stelem_I4);
			ilgen.Emit(OpCodes.Ldsfld, ForthStack);
			ilgen.Emit(OpCodes.Ldsfld, ForthStackIndex);
			ilgen.Emit(OpCodes.Ldc_I4_2);
			ilgen.Emit(OpCodes.Sub);
			ilgen.Emit(OpCodes.Ldsfld, Dummy1);
			ilgen.Emit(OpCodes.Stelem_I4);
		}

		// _Over - Duplicates the second value on the stack
		// Input:  ILGenerator for the method
		//		ForthStack[ForthStackIndex] = ForthStack[ForthStackIndex - 2];
		//		ForthStackIndex++;
		// Output: None
		private void _Over(ILGenerator ilgen)
		{
			ilgen.Emit(OpCodes.Ldsfld, ForthStack);
			ilgen.Emit(OpCodes.Ldsfld, ForthStackIndex);
			ilgen.Emit(OpCodes.Ldsfld, ForthStack);
			ilgen.Emit(OpCodes.Ldsfld, ForthStackIndex);
			ilgen.Emit(OpCodes.Ldc_I4_2);
			ilgen.Emit(OpCodes.Sub);
			ilgen.Emit(OpCodes.Ldelem_I4);
			ilgen.Emit(OpCodes.Stelem_I4);
			ilgen.Emit(OpCodes.Ldsfld, ForthStackIndex);
			ilgen.Emit(OpCodes.Ldc_I4_1);
			ilgen.Emit(OpCodes.Add);
			ilgen.Emit(OpCodes.Stsfld, ForthStackIndex);
		}

		// _Rot
		// Input:  ILGenerator for the method
		//		Dummy1 = ForthStack[ForthStackIndex - 3];
		//		ForthStack[ForthStackIndex - 3] = ForthStack[ForthStackIndex - 2];
		//		ForthStack[ForthStackIndex - 2] = ForthStack[ForthStackIndex - 1];
		//		ForthStack[ForthStackIndex - 1] = Dummy1;
		// Output: None
		private void _Rot(ILGenerator ilgen)
		{
			ilgen.Emit(OpCodes.Ldsfld, ForthStack);
			ilgen.Emit(OpCodes.Ldsfld, ForthStackIndex);
			ilgen.Emit(OpCodes.Ldc_I4_3);
			ilgen.Emit(OpCodes.Sub);
			ilgen.Emit(OpCodes.Ldelem_I4);
			ilgen.Emit(OpCodes.Stsfld, Dummy1);
			ilgen.Emit(OpCodes.Ldsfld, ForthStack);
			ilgen.Emit(OpCodes.Ldsfld, ForthStackIndex);
			ilgen.Emit(OpCodes.Ldc_I4_3);
			ilgen.Emit(OpCodes.Sub);
			ilgen.Emit(OpCodes.Ldsfld, ForthStack);
			ilgen.Emit(OpCodes.Ldsfld, ForthStackIndex);
			ilgen.Emit(OpCodes.Ldc_I4_2);
			ilgen.Emit(OpCodes.Sub);
			ilgen.Emit(OpCodes.Ldelem_I4);
			ilgen.Emit(OpCodes.Stelem_I4);
			ilgen.Emit(OpCodes.Ldsfld, ForthStack);
			ilgen.Emit(OpCodes.Ldsfld, ForthStackIndex);
			ilgen.Emit(OpCodes.Ldc_I4_2);
			ilgen.Emit(OpCodes.Sub);
			ilgen.Emit(OpCodes.Ldsfld, ForthStack);
			ilgen.Emit(OpCodes.Ldsfld, ForthStackIndex);
			ilgen.Emit(OpCodes.Ldc_I4_1);
			ilgen.Emit(OpCodes.Sub);
			ilgen.Emit(OpCodes.Ldelem_I4);
			ilgen.Emit(OpCodes.Stelem_I4);
			ilgen.Emit(OpCodes.Ldsfld, ForthStack);
			ilgen.Emit(OpCodes.Ldsfld, ForthStackIndex);
			ilgen.Emit(OpCodes.Ldc_I4_1);
			ilgen.Emit(OpCodes.Sub);
			ilgen.Emit(OpCodes.Ldsfld, Dummy1);
			ilgen.Emit(OpCodes.Stelem_I4);
		}

		// _SPfetch - Current Forth stack index
		// Input:  ILGenerator for the method
		//		ForthStack[ForthStackIndex] = ForthStackIndex;
		//		ForthStackIndex++;
		// Output: None
		private void _SPfetch(ILGenerator ilgen)
		{
			ilgen.Emit(OpCodes.Ldsfld, ForthStack);
			ilgen.Emit(OpCodes.Ldsfld, ForthStackIndex);
			ilgen.Emit(OpCodes.Ldsfld, ForthStackIndex);
			ilgen.Emit(OpCodes.Stelem_I4);
			ilgen.Emit(OpCodes.Ldsfld, ForthStackIndex);
			ilgen.Emit(OpCodes.Ldc_I4_1);
			ilgen.Emit(OpCodes.Add);
			ilgen.Emit(OpCodes.Stsfld, ForthStackIndex);
		}

		// _RPfetch - Current return stack index
		// Input:  ILGenerator for the method
		//		ForthStack[ForthStackIndex] = ReturnStackIndex;
		//		ForthStackIndex++;
		// Output: None
		private void _RPfetch(ILGenerator ilgen)
		{
			ilgen.Emit(OpCodes.Ldsfld, ForthStack);
			ilgen.Emit(OpCodes.Ldsfld, ForthStackIndex);
			ilgen.Emit(OpCodes.Ldsfld, ReturnStackIndex);
			ilgen.Emit(OpCodes.Stelem_I4);
			ilgen.Emit(OpCodes.Ldsfld, ForthStackIndex);
			ilgen.Emit(OpCodes.Ldc_I4_1);
			ilgen.Emit(OpCodes.Add);
			ilgen.Emit(OpCodes.Stsfld, ForthStackIndex);
		}

		// _SPstore - Flush Forth stack
		// Input:  ILGenerator for the method
		//		ForthStackIndex = ForthStackOrigin;
		// Output: None
		private void _SPstore(ILGenerator ilgen)
		{
			ilgen.Emit(OpCodes.Ldc_I4, ForthStackOrigin);
			ilgen.Emit(OpCodes.Stsfld, ForthStackIndex);
		}

		// _RPstore - Flush return stack
		// Input:  ILGenerator for the method
		//		ForthStackIndex = ReturnStackOrigin;
		// Output: None
		private void _RPstore(ILGenerator ilgen)
		{
			ilgen.Emit(OpCodes.Ldc_I4, ReturnStackOrigin);
			ilgen.Emit(OpCodes.Stsfld, ForthStackIndex);
		}
		
		// _Fetch - Fetch the value from a given address
		// Input:  ILGenerator for the method
		//		Dummy1 = ForthStack[ForthStackIndex - 1];
		//		ForthStack[ForthStackIndex - 1] = ForthStack[Dummy1];
		// Output: None
		private void _Fetch(ILGenerator ilgen)
		{
			ilgen.Emit(OpCodes.Ldsfld, ForthStack);
			ilgen.Emit(OpCodes.Ldsfld, ForthStackIndex);
			ilgen.Emit(OpCodes.Ldc_I4_1);
			ilgen.Emit(OpCodes.Sub);
			ilgen.Emit(OpCodes.Ldelem_I4);
			ilgen.Emit(OpCodes.Stsfld, Dummy1);
			ilgen.Emit(OpCodes.Ldsfld, ForthStack);
			ilgen.Emit(OpCodes.Ldsfld, ForthStackIndex);
			ilgen.Emit(OpCodes.Ldc_I4_1);
			ilgen.Emit(OpCodes.Sub);
			ilgen.Emit(OpCodes.Ldsfld, ForthStack);
			ilgen.Emit(OpCodes.Ldsfld, Dummy1);
			ilgen.Emit(OpCodes.Ldelem_I4);
			ilgen.Emit(OpCodes.Stelem_I4);
		}

		// _QuestionMark - Display the value from a given address
		// Input:  ILGenerator for the method
		//		Console.Write(ForthStack[ForthStack[--ForthStackIndex]]);
		// Output: None
		private void _QuestionMark(ILGenerator ilgen)
		{
			ilgen.Emit(OpCodes.Ldsfld, ForthStack);
			ilgen.Emit(OpCodes.Ldsfld, ForthStack);
			ilgen.Emit(OpCodes.Ldsfld, ForthStackIndex);
			ilgen.Emit(OpCodes.Ldc_I4_1);
			ilgen.Emit(OpCodes.Sub);
			ilgen.Emit(OpCodes.Dup);
			ilgen.Emit(OpCodes.Stsfld, ForthStackIndex);
			ilgen.Emit(OpCodes.Ldelem_I4);
			ilgen.Emit(OpCodes.Ldelem_I4);
			ilgen.Emit(OpCodes.Call, WriteIntMethod);
		}

		// _Store - Stores a value at a given address
		// Input:  ILGenerator for the method
		//		Dummy1 = ForthStack[ForthStackIndex - 2];
		//		Dummy2 = ForthStack[ForthStackIndex - 1];
		//		ForthStack[Dummy2] = Dummy1;
		//		ForthStackIndex -= 2;
		// Output: None
		private void _Store(ILGenerator ilgen)
		{
			ilgen.Emit(OpCodes.Ldsfld, ForthStack);
			ilgen.Emit(OpCodes.Ldsfld, ForthStackIndex);
			ilgen.Emit(OpCodes.Ldc_I4_2);
			ilgen.Emit(OpCodes.Sub);
			ilgen.Emit(OpCodes.Ldelem_I4);
			ilgen.Emit(OpCodes.Stsfld, Dummy1);
			ilgen.Emit(OpCodes.Ldsfld, ForthStack);
			ilgen.Emit(OpCodes.Ldsfld, ForthStackIndex);
			ilgen.Emit(OpCodes.Ldc_I4_1);
			ilgen.Emit(OpCodes.Sub);
			ilgen.Emit(OpCodes.Ldelem_I4);
			ilgen.Emit(OpCodes.Stsfld, Dummy2);
			ilgen.Emit(OpCodes.Ldsfld, ForthStack);
			ilgen.Emit(OpCodes.Ldsfld, Dummy2);
			ilgen.Emit(OpCodes.Ldsfld, Dummy1);
			ilgen.Emit(OpCodes.Stelem_I4);
			ilgen.Emit(OpCodes.Ldsfld, ForthStackIndex);
			ilgen.Emit(OpCodes.Ldc_I4_2);
			ilgen.Emit(OpCodes.Sub);
			ilgen.Emit(OpCodes.Stsfld, ForthStackIndex);
		}

		// _PlusStore - Adds a value at a given address
		// Input:  ILGenerator for the method
		//		Dummy1 = ForthStack[ForthStackIndex - 2];
		//		Dummy2 = ForthStack[ForthStackIndex - 1];
		//		ForthStack[Dummy2] += Dummy1;
		//		ForthStackIndex -= 2;
		// Output: None
		private void _PlusStore(ILGenerator ilgen)
		{
			ilgen.Emit(OpCodes.Ldsfld, ForthStack);
			ilgen.Emit(OpCodes.Ldsfld, ForthStackIndex);
			ilgen.Emit(OpCodes.Ldc_I4_2);
			ilgen.Emit(OpCodes.Sub);
			ilgen.Emit(OpCodes.Ldelem_I4);
			ilgen.Emit(OpCodes.Stsfld, Dummy1);
			ilgen.Emit(OpCodes.Ldsfld, ForthStack);
			ilgen.Emit(OpCodes.Ldsfld, ForthStackIndex);
			ilgen.Emit(OpCodes.Ldc_I4_1);
			ilgen.Emit(OpCodes.Sub);
			ilgen.Emit(OpCodes.Ldelem_I4);
			ilgen.Emit(OpCodes.Stsfld, Dummy2);
			ilgen.Emit(OpCodes.Ldsfld, ForthStack);
			ilgen.Emit(OpCodes.Ldsfld, Dummy2);
			ilgen.Emit(OpCodes.Ldelem_I4);
			ilgen.Emit(OpCodes.Stsfld, Dummy3);
			ilgen.Emit(OpCodes.Ldsfld, ForthStack);
			ilgen.Emit(OpCodes.Ldsfld, Dummy2);
			ilgen.Emit(OpCodes.Ldsfld, Dummy1);
			ilgen.Emit(OpCodes.Ldsfld, Dummy3);
			ilgen.Emit(OpCodes.Add);
			ilgen.Emit(OpCodes.Stelem_I4);
			ilgen.Emit(OpCodes.Ldsfld, ForthStackIndex);
			ilgen.Emit(OpCodes.Ldc_I4_2);
			ilgen.Emit(OpCodes.Sub);
			ilgen.Emit(OpCodes.Stsfld, ForthStackIndex);
		}

		// _Emit - Prints a char with the specified code
		// Input:  ILGenerator for the method
		//		Console.Write((char)ForthStack[--ForthStackIndex]);
		// Output: None
		private void _Emit(ILGenerator ilgen)
		{
			ilgen.Emit(OpCodes.Ldsfld, ForthStack);
			ilgen.Emit(OpCodes.Ldsfld, ForthStackIndex);
			ilgen.Emit(OpCodes.Ldc_I4_1);
			ilgen.Emit(OpCodes.Sub);
			ilgen.Emit(OpCodes.Dup);
			ilgen.Emit(OpCodes.Stsfld, ForthStackIndex);
			ilgen.Emit(OpCodes.Ldelem_I4);
			ilgen.Emit(OpCodes.Conv_U2);
			ilgen.Emit(OpCodes.Call, WriteCharMethod);
		}

		// _Spaces - Prints a series of spaces
		// Input:  ILGenerator for the method
		//		for(Dummy1 = 0; Dummy1 < ForthStack[--ForthStackIndex]; Dummy1++) Console.Write(' ');
		// Output: None
		private void _Spaces(ILGenerator ilgen)
		{
			Label lb1 = ilgen.DefineLabel();
			Label lb2 = ilgen.DefineLabel();

			ilgen.Emit(OpCodes.Ldsfld, ForthStackIndex);
			ilgen.Emit(OpCodes.Ldc_I4_1);
			ilgen.Emit(OpCodes.Sub);
			ilgen.Emit(OpCodes.Stsfld, ForthStackIndex);
			ilgen.Emit(OpCodes.Ldc_I4_0);
			ilgen.Emit(OpCodes.Stsfld, Dummy1);
			ilgen.Emit(OpCodes.Br_S, lb1);
			ilgen.MarkLabel(lb2);
			ilgen.Emit(OpCodes.Ldc_I4_S, 32);
			ilgen.Emit(OpCodes.Call, WriteCharMethod);
			ilgen.Emit(OpCodes.Ldsfld, Dummy1);
			ilgen.Emit(OpCodes.Ldc_I4_1);
			ilgen.Emit(OpCodes.Add);
			ilgen.Emit(OpCodes.Stsfld, Dummy1);
			ilgen.MarkLabel(lb1);
			ilgen.Emit(OpCodes.Ldsfld, Dummy1);
			ilgen.Emit(OpCodes.Ldsfld, ForthStack);
			ilgen.Emit(OpCodes.Ldsfld, ForthStackIndex);
			ilgen.Emit(OpCodes.Ldelem_I4);
			ilgen.Emit(OpCodes.Blt_S, lb2);
		}

		// _Type - Types a text
		// Input:  ILGenerator for the method
		//		Dummy1 = ForthStack[ForthStackIndex - 2];
		//		Dummy2 = ForthStack[ForthStackIndex - 1];
		//		for(Dummy3 = Dummy1; Dummy3 < Dummy1 + Dummy2; Dummy3++) Console.Write((char)ForthStack[Dummy3]);
		//		ForthStackIndex -= 2;
		// Output: None
		private void _Type(ILGenerator ilgen)
		{
			Label lb1 = ilgen.DefineLabel();
			Label lb2 = ilgen.DefineLabel();

			ilgen.Emit(OpCodes.Ldsfld, ForthStack);
			ilgen.Emit(OpCodes.Ldsfld, ForthStackIndex);
			ilgen.Emit(OpCodes.Ldc_I4_2);
			ilgen.Emit(OpCodes.Sub);
			ilgen.Emit(OpCodes.Ldelem_I4);
			ilgen.Emit(OpCodes.Stsfld, Dummy1);
			ilgen.Emit(OpCodes.Ldsfld, ForthStack);
			ilgen.Emit(OpCodes.Ldsfld, ForthStackIndex);
			ilgen.Emit(OpCodes.Ldc_I4_1);
			ilgen.Emit(OpCodes.Sub);
			ilgen.Emit(OpCodes.Ldelem_I4);
			ilgen.Emit(OpCodes.Stsfld, Dummy2);
			ilgen.Emit(OpCodes.Ldsfld, Dummy1);
			ilgen.Emit(OpCodes.Stsfld, Dummy3);
			ilgen.Emit(OpCodes.Br_S, lb1);
			ilgen.MarkLabel(lb2);
			ilgen.Emit(OpCodes.Ldsfld, ForthStack);
			ilgen.Emit(OpCodes.Ldsfld, Dummy3);
			ilgen.Emit(OpCodes.Ldelem_I4);
			ilgen.Emit(OpCodes.Conv_U2);
			ilgen.Emit(OpCodes.Call, WriteCharMethod);
			ilgen.Emit(OpCodes.Ldsfld, Dummy3);
			ilgen.Emit(OpCodes.Ldc_I4_1);
			ilgen.Emit(OpCodes.Add);
			ilgen.Emit(OpCodes.Stsfld, Dummy3);
			ilgen.MarkLabel(lb1);
			ilgen.Emit(OpCodes.Ldsfld, Dummy3);
			ilgen.Emit(OpCodes.Ldsfld, Dummy1);
			ilgen.Emit(OpCodes.Ldsfld, Dummy2);
			ilgen.Emit(OpCodes.Add);
			ilgen.Emit(OpCodes.Blt_S, lb2);
			ilgen.Emit(OpCodes.Ldsfld, ForthStackIndex);
			ilgen.Emit(OpCodes.Ldc_I4_2);
			ilgen.Emit(OpCodes.Sub);
			ilgen.Emit(OpCodes.Stsfld, ForthStackIndex);
		}

		// _Pad - Pointer to a 64-cell area
		// Input:  ILGenerator for the method
		//		ForthStack[ForthStackIndex++] = Pad;
		// Output: None
		private void _Pad(ILGenerator ilgen)
		{
			ilgen.Emit(OpCodes.Ldsfld, ForthStack);
			ilgen.Emit(OpCodes.Ldsfld, ForthStackIndex);
			ilgen.Emit(OpCodes.Dup);
			ilgen.Emit(OpCodes.Ldc_I4_1);
			ilgen.Emit(OpCodes.Add);
			ilgen.Emit(OpCodes.Stsfld, ForthStackIndex);
			ilgen.Emit(OpCodes.Ldc_I4, Pad);	// PAD is at the end of the Forth stack
			ilgen.Emit(OpCodes.Stelem_I4);
		}

		// _Tib - Pointer to a 80-cell area
		// Input:  ILGenerator for the method
		//		ForthStack[ForthStackIndex++] = Tib;
		// Output: None
		private void _Tib(ILGenerator ilgen)
		{
			ilgen.Emit(OpCodes.Ldsfld, ForthStack);
			ilgen.Emit(OpCodes.Ldsfld, ForthStackIndex);
			ilgen.Emit(OpCodes.Dup);
			ilgen.Emit(OpCodes.Ldc_I4_1);
			ilgen.Emit(OpCodes.Add);
			ilgen.Emit(OpCodes.Stsfld, ForthStackIndex);
			ilgen.Emit(OpCodes.Ldc_I4, Tib);	// TIB is before the PAD area
			ilgen.Emit(OpCodes.Stelem_I4);			
		}

		// _StackOrigin - Forth stack and return stack origin
		// Input:  ILGenerator for the method
		//		ForthStack[ForthStackIndex++] = "ForthStackOrigin | ReturnStackOrigin";
		// Output: None
		private void _StackOrigin(ILGenerator ilgen, bool bForthStack)
		{
			ilgen.Emit(OpCodes.Ldsfld, ForthStack);
			ilgen.Emit(OpCodes.Ldsfld, ForthStackIndex);
			ilgen.Emit(OpCodes.Dup);
			ilgen.Emit(OpCodes.Ldc_I4_1);
			ilgen.Emit(OpCodes.Add);
			ilgen.Emit(OpCodes.Stsfld, ForthStackIndex);
			if(bForthStack)
				ilgen.Emit(OpCodes.Ldc_I4, ForthStackOrigin);
			else
				ilgen.Emit(OpCodes.Ldc_I4, ReturnStackOrigin);
			ilgen.Emit(OpCodes.Stelem_I4);			
		}

		// _Key - Places on the stack the code of the key pressed
		// Input:  ILGenerator for the method
		//		ForthStack[ForthStackIndex++] = Console.Read();		
		// Output: None
		private void _Key(ILGenerator ilgen)
		{
			MethodInfo ReadCharMethod = typeof(Console).GetMethod("Read");
			ilgen.Emit(OpCodes.Ldsfld, ForthStack);
			ilgen.Emit(OpCodes.Ldsfld, ForthStackIndex);
			ilgen.Emit(OpCodes.Dup);
			ilgen.Emit(OpCodes.Ldc_I4_1);
			ilgen.Emit(OpCodes.Add);
			ilgen.Emit(OpCodes.Stsfld, ForthStackIndex);
			ilgen.Emit(OpCodes.Call, ReadCharMethod);
			ilgen.Emit(OpCodes.Stelem_I4);
		}

		// _Expect - Awaits characters and places them on the stack
		// Input:  ILGenerator for the method
		//		Dummy1 = ForthStack[--ForthStackIndex];	// Max number of characters
		//		Dummy2 = ForthStack[--ForthStackIndex];	// Address
		//		while(Dummy1 > 0)
		//		{
		//			Dummy3 = Console.Read();
		//			if(Dummy3 == 13) 
		//			{
		//				ForthStack[Dummy2++] = 0;
		//				break;
		//			}
		//			ForthStack[Dummy2++] = Dummy3;
		//			Dummy1--;
		//		}
		// Output: None
		private void _Expect(ILGenerator ilgen)
		{
			MethodInfo ReadCharMethod = typeof(Console).GetMethod("Read");
			Label lb1 = ilgen.DefineLabel();
			Label lb2 = ilgen.DefineLabel();
			Label lb3 = ilgen.DefineLabel();
			Label lb4 = ilgen.DefineLabel();

			ilgen.Emit(OpCodes.Ldsfld, ForthStack);
			ilgen.Emit(OpCodes.Ldsfld, ForthStackIndex);
			ilgen.Emit(OpCodes.Ldc_I4_1);
			ilgen.Emit(OpCodes.Sub);
			ilgen.Emit(OpCodes.Dup);
			ilgen.Emit(OpCodes.Stsfld, ForthStackIndex);
			ilgen.Emit(OpCodes.Ldelem_I4);
			ilgen.Emit(OpCodes.Stsfld, Dummy1);
			ilgen.Emit(OpCodes.Ldsfld, ForthStack);
			ilgen.Emit(OpCodes.Ldsfld, ForthStackIndex);
			ilgen.Emit(OpCodes.Ldc_I4_1);
			ilgen.Emit(OpCodes.Sub);
			ilgen.Emit(OpCodes.Dup);
			ilgen.Emit(OpCodes.Stsfld, ForthStackIndex);
			ilgen.Emit(OpCodes.Ldelem_I4);
			ilgen.Emit(OpCodes.Stsfld, Dummy2);
			ilgen.Emit(OpCodes.Br_S, lb1);
			ilgen.MarkLabel(lb4);
			ilgen.Emit(OpCodes.Call, ReadCharMethod);
			ilgen.Emit(OpCodes.Stsfld, Dummy3);
			ilgen.Emit(OpCodes.Ldsfld, Dummy3);
			ilgen.Emit(OpCodes.Ldc_I4, 13);
			ilgen.Emit(OpCodes.Bne_Un_S, lb2);
			ilgen.Emit(OpCodes.Ldsfld, ForthStack);
			ilgen.Emit(OpCodes.Ldsfld, Dummy2);
			ilgen.Emit(OpCodes.Dup);
			ilgen.Emit(OpCodes.Ldc_I4_1);
			ilgen.Emit(OpCodes.Add);
			ilgen.Emit(OpCodes.Stsfld, Dummy2);
			ilgen.Emit(OpCodes.Ldc_I4_0);
			ilgen.Emit(OpCodes.Stelem_I4);
			ilgen.Emit(OpCodes.Br_S, lb3);
			ilgen.MarkLabel(lb2);
			ilgen.Emit(OpCodes.Ldsfld, ForthStack);
			ilgen.Emit(OpCodes.Ldsfld, Dummy2);
			ilgen.Emit(OpCodes.Dup);
			ilgen.Emit(OpCodes.Ldc_I4_1);
			ilgen.Emit(OpCodes.Add);
			ilgen.Emit(OpCodes.Stsfld, Dummy2);
			ilgen.Emit(OpCodes.Ldsfld, Dummy3);
			ilgen.Emit(OpCodes.Stelem_I4);
			ilgen.Emit(OpCodes.Ldsfld, Dummy1);
			ilgen.Emit(OpCodes.Ldc_I4_1);
			ilgen.Emit(OpCodes.Sub);
			ilgen.Emit(OpCodes.Stsfld, Dummy1);
			ilgen.MarkLabel(lb1);
			ilgen.Emit(OpCodes.Ldsfld, Dummy1);
			ilgen.Emit(OpCodes.Ldc_I4_0);
			ilgen.Emit(OpCodes.Bgt_S, lb4);
			ilgen.MarkLabel(lb3);
		}

		// _Query - Awaits 80 characters at the TIB area
		// Input:  ILGenerator for the method
		//		Dummy1 = 80;	// Max number of characters
		//		Dummy2 = Tib;	// Address
		//		while(Dummy1 > 0)
		//		{
		//			Dummy3 = Console.Read();
		//			if(Dummy3 == 13) 
		//			{
		//				ForthStack[Dummy2++] = 0;
		//				break;
		//			}
		//			ForthStack[Dummy2++] = Dummy3;
		//			Dummy1--;
		//		}
		// Output: None
		private void _Query(ILGenerator ilgen)
		{
			MethodInfo ReadCharMethod = typeof(Console).GetMethod("Read");
			Label lb1 = ilgen.DefineLabel();
			Label lb2 = ilgen.DefineLabel();
			Label lb3 = ilgen.DefineLabel();
			Label lb4 = ilgen.DefineLabel();

			ilgen.Emit(OpCodes.Ldc_I4_S, 80);
			ilgen.Emit(OpCodes.Stsfld, Dummy1);
			ilgen.Emit(OpCodes.Ldc_I4, Tib);
			ilgen.Emit(OpCodes.Stsfld, Dummy2);
			ilgen.Emit(OpCodes.Br_S, lb1);
			ilgen.MarkLabel(lb4);
			ilgen.Emit(OpCodes.Call, ReadCharMethod);
			ilgen.Emit(OpCodes.Stsfld, Dummy3);
			ilgen.Emit(OpCodes.Ldsfld, Dummy3);
			ilgen.Emit(OpCodes.Ldc_I4, 13);
			ilgen.Emit(OpCodes.Bne_Un_S, lb2);
			ilgen.Emit(OpCodes.Ldsfld, ForthStack);
			ilgen.Emit(OpCodes.Ldsfld, Dummy2);
			ilgen.Emit(OpCodes.Dup);
			ilgen.Emit(OpCodes.Ldc_I4_1);
			ilgen.Emit(OpCodes.Add);
			ilgen.Emit(OpCodes.Stsfld, Dummy2);
			ilgen.Emit(OpCodes.Ldc_I4_0);
			ilgen.Emit(OpCodes.Stelem_I4);
			ilgen.Emit(OpCodes.Br_S, lb3);
			ilgen.MarkLabel(lb2);
			ilgen.Emit(OpCodes.Ldsfld, ForthStack);
			ilgen.Emit(OpCodes.Ldsfld, Dummy2);
			ilgen.Emit(OpCodes.Dup);
			ilgen.Emit(OpCodes.Ldc_I4_1);
			ilgen.Emit(OpCodes.Add);
			ilgen.Emit(OpCodes.Stsfld, Dummy2);
			ilgen.Emit(OpCodes.Ldsfld, Dummy3);
			ilgen.Emit(OpCodes.Stelem_I4);
			ilgen.Emit(OpCodes.Ldsfld, Dummy1);
			ilgen.Emit(OpCodes.Ldc_I4_1);
			ilgen.Emit(OpCodes.Sub);
			ilgen.Emit(OpCodes.Stsfld, Dummy1);
			ilgen.MarkLabel(lb1);
			ilgen.Emit(OpCodes.Ldsfld, Dummy1);
			ilgen.Emit(OpCodes.Ldc_I4_0);
			ilgen.Emit(OpCodes.Bgt_S, lb4);
			ilgen.MarkLabel(lb3);
		}

		// _ToR - Transfers the element to the top of the return stack
		// Input:  ILGenerator for the method
		//		ReturnStack[ReturnStackIndex++] = ForthStack[--ForthStackIndex];
		// Output: None
		private void _ToR(ILGenerator ilgen)
		{
			ilgen.Emit(OpCodes.Ldsfld, ReturnStack);
			ilgen.Emit(OpCodes.Ldsfld, ReturnStackIndex);
			ilgen.Emit(OpCodes.Dup);
			ilgen.Emit(OpCodes.Ldc_I4_1);
			ilgen.Emit(OpCodes.Add);
			ilgen.Emit(OpCodes.Stsfld, ReturnStackIndex);
			ilgen.Emit(OpCodes.Ldsfld, ForthStack);
			ilgen.Emit(OpCodes.Ldsfld, ForthStackIndex);
			ilgen.Emit(OpCodes.Ldc_I4_1);
			ilgen.Emit(OpCodes.Sub);
			ilgen.Emit(OpCodes.Dup);
			ilgen.Emit(OpCodes.Stsfld, ForthStackIndex);
			ilgen.Emit(OpCodes.Ldelem_I4);
			ilgen.Emit(OpCodes.Stelem_I4);
		}

		// _RFrom - Transfers the element from the return stack to the forth stack
		// Input:  ILGenerator for the method
		//		ForthStack[ForthStackIndex++] = ReturnStack[--ReturnStackIndex];
		// Output: None
		private void _RFrom(ILGenerator ilgen)
		{
			ilgen.Emit(OpCodes.Ldsfld, ForthStack);
			ilgen.Emit(OpCodes.Ldsfld, ForthStackIndex);
			ilgen.Emit(OpCodes.Dup);
			ilgen.Emit(OpCodes.Ldc_I4_1);
			ilgen.Emit(OpCodes.Add);
			ilgen.Emit(OpCodes.Stsfld, ForthStackIndex);
			ilgen.Emit(OpCodes.Ldsfld, ReturnStack);
			ilgen.Emit(OpCodes.Ldsfld, ReturnStackIndex);
			ilgen.Emit(OpCodes.Ldc_I4_1);
			ilgen.Emit(OpCodes.Sub);
			ilgen.Emit(OpCodes.Dup);
			ilgen.Emit(OpCodes.Stsfld, ReturnStackIndex);
			ilgen.Emit(OpCodes.Ldelem_I4);
			ilgen.Emit(OpCodes.Stelem_I4);
		}

		// _I - Copies the element from the return stack to the Forth stack
		// Input:  ILGenerator for the method
		//		ForthStack[ForthStackIndex++] = ReturnStack[ReturnStackIndex - 1];
		// Output: None
		private void _I(ILGenerator ilgen)
		{
			ilgen.Emit(OpCodes.Ldsfld, ForthStack);
			ilgen.Emit(OpCodes.Ldsfld, ForthStackIndex);
			ilgen.Emit(OpCodes.Dup);
			ilgen.Emit(OpCodes.Ldc_I4_1);
			ilgen.Emit(OpCodes.Add);
			ilgen.Emit(OpCodes.Stsfld, ForthStackIndex);
			ilgen.Emit(OpCodes.Ldsfld, ReturnStack);
			ilgen.Emit(OpCodes.Ldsfld, ReturnStackIndex);
			ilgen.Emit(OpCodes.Ldc_I4_1);
			ilgen.Emit(OpCodes.Sub);
			ilgen.Emit(OpCodes.Ldelem_I4);
			ilgen.Emit(OpCodes.Stelem_I4);
		}

		// _Fill - Fills an area
		// Input:  ILGenerator for the method
		//		Dummy1 = ForthStack[ForthStackIndex - 1];	// c
		//		Dummy2 = ForthStack[ForthStackIndex - 2];	// n
		//		Dummy3 = ForthStack[ForthStackIndex - 3];	// addr
		//		ForthStackIndex -= 3;
		//		while(Dummy2-- > 0)
		//		{
		//			ForthStack[Dummy3++] = Dummy1;
		//		}
		// Output: None
		private void _Fill(ILGenerator ilgen)
		{
			Label lb1 = ilgen.DefineLabel();
			Label lb2 = ilgen.DefineLabel();

			ilgen.Emit(OpCodes.Ldsfld, ForthStack);
			ilgen.Emit(OpCodes.Ldsfld, ForthStackIndex);
			ilgen.Emit(OpCodes.Ldc_I4_1);
			ilgen.Emit(OpCodes.Sub);
			ilgen.Emit(OpCodes.Ldelem_I4);
			ilgen.Emit(OpCodes.Stsfld, Dummy1);
			ilgen.Emit(OpCodes.Ldsfld, ForthStack);
			ilgen.Emit(OpCodes.Ldsfld, ForthStackIndex);
			ilgen.Emit(OpCodes.Ldc_I4_2);
			ilgen.Emit(OpCodes.Sub);
			ilgen.Emit(OpCodes.Ldelem_I4);
			ilgen.Emit(OpCodes.Stsfld, Dummy2);
			ilgen.Emit(OpCodes.Ldsfld, ForthStack);
			ilgen.Emit(OpCodes.Ldsfld, ForthStackIndex);
			ilgen.Emit(OpCodes.Ldc_I4_3);
			ilgen.Emit(OpCodes.Sub);
			ilgen.Emit(OpCodes.Ldelem_I4);
			ilgen.Emit(OpCodes.Stsfld, Dummy3);
			ilgen.Emit(OpCodes.Ldsfld, ForthStackIndex);
			ilgen.Emit(OpCodes.Ldc_I4_3);
			ilgen.Emit(OpCodes.Sub);
			ilgen.Emit(OpCodes.Stsfld, ForthStackIndex);
			ilgen.Emit(OpCodes.Br_S, lb1);
			ilgen.MarkLabel(lb2);
			ilgen.Emit(OpCodes.Ldsfld, ForthStack);
			ilgen.Emit(OpCodes.Ldsfld, Dummy3);
			ilgen.Emit(OpCodes.Dup);
			ilgen.Emit(OpCodes.Ldc_I4_1);
			ilgen.Emit(OpCodes.Add);
			ilgen.Emit(OpCodes.Stsfld, Dummy3);
			ilgen.Emit(OpCodes.Ldsfld, Dummy1);
			ilgen.Emit(OpCodes.Stelem_I4);
			ilgen.MarkLabel(lb1);
			ilgen.Emit(OpCodes.Ldsfld, Dummy2);
			ilgen.Emit(OpCodes.Dup);
			ilgen.Emit(OpCodes.Ldc_I4_1);
			ilgen.Emit(OpCodes.Sub);
			ilgen.Emit(OpCodes.Stsfld, Dummy2);
			ilgen.Emit(OpCodes.Ldc_I4_0);
			ilgen.Emit(OpCodes.Bgt_S, lb2);
		}

		// _Erase - Fills an area
		// Input:  ILGenerator for the method
		//		Dummy1 = ForthStack[ForthStackIndex - 1];	// n
		//		Dummy2 = ForthStack[ForthStackIndex - 2];	// addr
		//		ForthStackIndex -= 2;
		//		while(Dummy1-- > 0)
		//		{
		//			ForthStack[Dummy2++] = "Value"; // 0 or 32
		//		}
		// Output: None
		private void _Erase(ILGenerator ilgen, int FillValue)
		{
			Label lb1 = ilgen.DefineLabel();
			Label lb2 = ilgen.DefineLabel();

			ilgen.Emit(OpCodes.Ldsfld, ForthStack);
			ilgen.Emit(OpCodes.Ldsfld, ForthStackIndex);
			ilgen.Emit(OpCodes.Ldc_I4_1);
			ilgen.Emit(OpCodes.Sub);
			ilgen.Emit(OpCodes.Ldelem_I4);
			ilgen.Emit(OpCodes.Stsfld, Dummy1);
			ilgen.Emit(OpCodes.Ldsfld, ForthStack);
			ilgen.Emit(OpCodes.Ldsfld, ForthStackIndex);
			ilgen.Emit(OpCodes.Ldc_I4_2);
			ilgen.Emit(OpCodes.Sub);
			ilgen.Emit(OpCodes.Ldelem_I4);
			ilgen.Emit(OpCodes.Stsfld, Dummy2);
			ilgen.Emit(OpCodes.Ldsfld, ForthStackIndex);
			ilgen.Emit(OpCodes.Ldc_I4_2);
			ilgen.Emit(OpCodes.Sub);
			ilgen.Emit(OpCodes.Stsfld, ForthStackIndex);
			ilgen.Emit(OpCodes.Br_S, lb1);
			ilgen.MarkLabel(lb2);
			ilgen.Emit(OpCodes.Ldsfld, ForthStack);
			ilgen.Emit(OpCodes.Ldsfld, Dummy2);
			ilgen.Emit(OpCodes.Dup);
			ilgen.Emit(OpCodes.Ldc_I4_1);
			ilgen.Emit(OpCodes.Add);
			ilgen.Emit(OpCodes.Stsfld, Dummy2);
			ilgen.Emit(OpCodes.Ldc_I4, FillValue);
			ilgen.Emit(OpCodes.Stelem_I4);
			ilgen.MarkLabel(lb1);
			ilgen.Emit(OpCodes.Ldsfld, Dummy1);
			ilgen.Emit(OpCodes.Dup);
			ilgen.Emit(OpCodes.Ldc_I4_1);
			ilgen.Emit(OpCodes.Sub);
			ilgen.Emit(OpCodes.Stsfld, Dummy1);
			ilgen.Emit(OpCodes.Ldc_I4_0);
			ilgen.Emit(OpCodes.Bgt_S, lb2);
		}

		// _Str2Int - Converts a string from TIB to a number on the stack
		// Input:  ILGenerator for the method
		//		Dummy1 = Tib;
		//		strDummy = "";
		//		while(ForthStack[Dummy1] != 0) 
		//		{	
		//			strDummy += (char)ForthStack[Dummy1];
		//			Dummy1++;
		//		}
		//		ForthStack[ForthStackIndex++] = Convert.ToInt32(strDummy);
		// Output: None
		private void _Str2Int(ILGenerator ilgen)
		{
			Label lb1 = ilgen.DefineLabel();
			Label lb2 = ilgen.DefineLabel();
			Label exc = ilgen.DefineLabel();
			
			ilgen.BeginExceptionBlock();
			ilgen.Emit(OpCodes.Ldc_I4, Tib);
			ilgen.Emit(OpCodes.Stsfld, Dummy1);
			ilgen.Emit(OpCodes.Ldstr, "");
			ilgen.Emit(OpCodes.Stsfld, strDummy);
			ilgen.Emit(OpCodes.Br_S, lb1);
			ilgen.MarkLabel(lb2);
			ilgen.Emit(OpCodes.Ldsfld, strDummy);
			ilgen.Emit(OpCodes.Ldsfld, ForthStack);
			ilgen.Emit(OpCodes.Ldsfld, Dummy1);
			ilgen.Emit(OpCodes.Ldelem_I4);
			ilgen.Emit(OpCodes.Conv_U2);
			ilgen.Emit(OpCodes.Box, typeof(char));
			ilgen.Emit(OpCodes.Call, typeof(string).GetMethod("Concat", new Type[] { typeof(Object), typeof(Object) }));
			ilgen.Emit(OpCodes.Stsfld, strDummy);
			ilgen.Emit(OpCodes.Ldsfld, Dummy1);
			ilgen.Emit(OpCodes.Ldc_I4_1);
			ilgen.Emit(OpCodes.Add);
			ilgen.Emit(OpCodes.Stsfld, Dummy1);
			ilgen.MarkLabel(lb1);
			ilgen.Emit(OpCodes.Ldsfld, ForthStack);
			ilgen.Emit(OpCodes.Ldsfld, Dummy1);
			ilgen.Emit(OpCodes.Ldelem_I4);
			ilgen.Emit(OpCodes.Brtrue_S, lb2);
			ilgen.Emit(OpCodes.Ldsfld, ForthStack);
			ilgen.Emit(OpCodes.Ldsfld, ForthStackIndex);
			ilgen.Emit(OpCodes.Dup);
			ilgen.Emit(OpCodes.Ldc_I4_1);
			ilgen.Emit(OpCodes.Add);
			ilgen.Emit(OpCodes.Stsfld, ForthStackIndex);
			ilgen.Emit(OpCodes.Ldsfld, strDummy);
			ilgen.Emit(OpCodes.Call, typeof(Convert).GetMethod("ToInt32", new Type[] { typeof(string) }));
			ilgen.Emit(OpCodes.Stelem_I4);
			
			ilgen.BeginCatchBlock(typeof(FormatException));
			ilgen.EmitWriteLine("RUNTIME ERROR: Could not interpret the TIB area.");
			ilgen.Emit(OpCodes.Pop);
			ilgen.Emit(OpCodes.Ret);
			ilgen.EndExceptionBlock();
		}

		// _Count - Counts the number of non-zero consecutive characters
		// Input:  ILGenerator for the method
		//		Dummy1 = Tib;
		//		strDummy = "";
		//		while(ForthStack[Dummy1] != 0) 
		//		{	
		//			strDummy += (char)ForthStack[Dummy1];
		//			Dummy1++;
		//		}
		//		ForthStack[ForthStackIndex++] = Convert.ToInt32(strDummy);
		// Output: None
		private void _Count(ILGenerator ilgen)
		{
			Label lb1 = ilgen.DefineLabel();
			Label lb2 = ilgen.DefineLabel();
			
			ilgen.Emit(OpCodes.Ldc_I4_0);
			ilgen.Emit(OpCodes.Stsfld, Dummy1);
			ilgen.Emit(OpCodes.Ldsfld, ForthStack);
			ilgen.Emit(OpCodes.Ldsfld, ForthStackIndex);
			ilgen.Emit(OpCodes.Ldc_I4_1);
			ilgen.Emit(OpCodes.Sub);
			ilgen.Emit(OpCodes.Ldelem_I4);
			ilgen.Emit(OpCodes.Stsfld, Dummy2);
			ilgen.Emit(OpCodes.Br_S, lb1);
			ilgen.MarkLabel(lb2);
			ilgen.Emit(OpCodes.Ldsfld, Dummy1);
			ilgen.Emit(OpCodes.Ldc_I4_1);
			ilgen.Emit(OpCodes.Add);
			ilgen.Emit(OpCodes.Stsfld, Dummy1);
			ilgen.MarkLabel(lb1);
			ilgen.Emit(OpCodes.Ldsfld, ForthStack);
			ilgen.Emit(OpCodes.Ldsfld, Dummy2);
			ilgen.Emit(OpCodes.Dup);
			ilgen.Emit(OpCodes.Ldc_I4_1);
			ilgen.Emit(OpCodes.Add);
			ilgen.Emit(OpCodes.Stsfld, Dummy2);
			ilgen.Emit(OpCodes.Ldelem_I4);
			ilgen.Emit(OpCodes.Brtrue_S, lb2);
			ilgen.Emit(OpCodes.Ldsfld, ForthStack);
			ilgen.Emit(OpCodes.Ldsfld, ForthStackIndex);
			ilgen.Emit(OpCodes.Ldc_I4_1);
			ilgen.Emit(OpCodes.Sub);
			ilgen.Emit(OpCodes.Ldsfld, Dummy1);
			ilgen.Emit(OpCodes.Stelem_I4);
		}

		// _Dump - Dumps a string on the stack
		// Input:  ILGenerator for the method, string to dump on the stack
		//		Dummy1 = ForthStack[--ForthStackIndex];		// Address
		//		Dummy2 = strDummy.Length;
		//		Dummy3 = 0;
		//		while(Dummy2-- > 0)
		//		{
		//			ForthStack[Dummy1++] = strDummy[Dummy3++];
		//		}
		//		ForthStack[Dummy1] = 0;		
		// Output: None
		private void _Dump(ILGenerator ilgen, string text)
		{
			Label lb1 = ilgen.DefineLabel();
			Label lb2 = ilgen.DefineLabel();
			
			ilgen.Emit(OpCodes.Ldstr, text);
			ilgen.Emit(OpCodes.Stsfld, strDummy);

			ilgen.Emit(OpCodes.Ldsfld, ForthStack);
			ilgen.Emit(OpCodes.Ldsfld, ForthStackIndex);
			ilgen.Emit(OpCodes.Ldc_I4_1);
			ilgen.Emit(OpCodes.Sub);
			ilgen.Emit(OpCodes.Dup);
			ilgen.Emit(OpCodes.Stsfld, ForthStackIndex);
			ilgen.Emit(OpCodes.Ldelem_I4);
			ilgen.Emit(OpCodes.Stsfld, Dummy1);
			ilgen.Emit(OpCodes.Ldsfld, strDummy);
			ilgen.Emit(OpCodes.Callvirt, typeof(string).GetMethod("get_Length"));
			ilgen.Emit(OpCodes.Stsfld, Dummy2);
			ilgen.Emit(OpCodes.Ldc_I4_0);
			ilgen.Emit(OpCodes.Stsfld, Dummy3);
			ilgen.Emit(OpCodes.Br_S, lb1);
			ilgen.MarkLabel(lb2);
			ilgen.Emit(OpCodes.Ldsfld, ForthStack);
			ilgen.Emit(OpCodes.Ldsfld, Dummy1);
			ilgen.Emit(OpCodes.Dup);
			ilgen.Emit(OpCodes.Ldc_I4_1);
			ilgen.Emit(OpCodes.Add);
			ilgen.Emit(OpCodes.Stsfld, Dummy1);
			ilgen.Emit(OpCodes.Ldsfld, strDummy);
			ilgen.Emit(OpCodes.Ldsfld, Dummy3);
			ilgen.Emit(OpCodes.Dup);
			ilgen.Emit(OpCodes.Ldc_I4_1);
			ilgen.Emit(OpCodes.Add);
			ilgen.Emit(OpCodes.Stsfld, Dummy3);
			ilgen.Emit(OpCodes.Callvirt, typeof(string).GetMethod("get_Chars", new Type[] { typeof(int) }));
			ilgen.Emit(OpCodes.Stelem_I4);
			ilgen.MarkLabel(lb1);
			ilgen.Emit(OpCodes.Ldsfld, Dummy2);
			ilgen.Emit(OpCodes.Dup);
			ilgen.Emit(OpCodes.Ldc_I4_1);
			ilgen.Emit(OpCodes.Sub);
			ilgen.Emit(OpCodes.Stsfld, Dummy2);
			ilgen.Emit(OpCodes.Ldc_I4_0);
			ilgen.Emit(OpCodes.Bgt_S, lb2);
			ilgen.Emit(OpCodes.Ldsfld, ForthStack);
			ilgen.Emit(OpCodes.Ldsfld, Dummy1);
			ilgen.Emit(OpCodes.Ldc_I4_0);
			ilgen.Emit(OpCodes.Stelem_I4);
		}

		// _CMove - Dumps a string on the stack
		// Input:  ILGenerator for the method
		//		Dummy1 = ForthStack[--ForthStackIndex];	// Count
		//		Dummy2 = ForthStack[--ForthStackIndex];	// Destination
		//		Dummy3 = ForthStack[--ForthStackIndex];	// Source
		//		while(Dummy1-- > 0) ForthStack[Dummy2++] = ForthStack[Dummy3++];		
		// Output: None
		private void _CMove(ILGenerator ilgen)
		{
			Label lb1 = ilgen.DefineLabel();
			Label lb2 = ilgen.DefineLabel();
			
			ilgen.Emit(OpCodes.Ldsfld, ForthStack);
			ilgen.Emit(OpCodes.Ldsfld, ForthStackIndex);
			ilgen.Emit(OpCodes.Ldc_I4_1);
			ilgen.Emit(OpCodes.Sub);
			ilgen.Emit(OpCodes.Dup);
			ilgen.Emit(OpCodes.Stsfld, ForthStackIndex);
			ilgen.Emit(OpCodes.Ldelem_I4);
			ilgen.Emit(OpCodes.Stsfld, Dummy1);
			ilgen.Emit(OpCodes.Ldsfld, ForthStack);
			ilgen.Emit(OpCodes.Ldsfld, ForthStackIndex);
			ilgen.Emit(OpCodes.Ldc_I4_1);
			ilgen.Emit(OpCodes.Sub);
			ilgen.Emit(OpCodes.Dup);
			ilgen.Emit(OpCodes.Stsfld, ForthStackIndex);
			ilgen.Emit(OpCodes.Ldelem_I4);
			ilgen.Emit(OpCodes.Stsfld, Dummy2);
			ilgen.Emit(OpCodes.Ldsfld, ForthStack);
			ilgen.Emit(OpCodes.Ldsfld, ForthStackIndex);
			ilgen.Emit(OpCodes.Ldc_I4_1);
			ilgen.Emit(OpCodes.Sub);
			ilgen.Emit(OpCodes.Dup);
			ilgen.Emit(OpCodes.Stsfld, ForthStackIndex);
			ilgen.Emit(OpCodes.Ldelem_I4);
			ilgen.Emit(OpCodes.Stsfld, Dummy3);
			ilgen.Emit(OpCodes.Br_S, lb1);
			ilgen.MarkLabel(lb2);
			ilgen.Emit(OpCodes.Ldsfld, ForthStack);
			ilgen.Emit(OpCodes.Ldsfld, Dummy2);
			ilgen.Emit(OpCodes.Dup);
			ilgen.Emit(OpCodes.Ldc_I4_1);
			ilgen.Emit(OpCodes.Add);
			ilgen.Emit(OpCodes.Stsfld, Dummy2);
			ilgen.Emit(OpCodes.Ldsfld, ForthStack);
			ilgen.Emit(OpCodes.Ldsfld, Dummy3);
			ilgen.Emit(OpCodes.Dup);
			ilgen.Emit(OpCodes.Ldc_I4_1);
			ilgen.Emit(OpCodes.Add);
			ilgen.Emit(OpCodes.Stsfld, Dummy3);
			ilgen.Emit(OpCodes.Ldelem_I4);
			ilgen.Emit(OpCodes.Stelem_I4);
			ilgen.MarkLabel(lb1);
			ilgen.Emit(OpCodes.Ldsfld, Dummy1);
			ilgen.Emit(OpCodes.Dup);
			ilgen.Emit(OpCodes.Ldc_I4_1);
			ilgen.Emit(OpCodes.Sub);
			ilgen.Emit(OpCodes.Stsfld, Dummy1);
			ilgen.Emit(OpCodes.Ldc_I4_0);
			ilgen.Emit(OpCodes.Bgt_S, lb2);
		}

		// _Int2Str	- Converts an integer on the stack to a string
		// Input:  ILGenerator for the method
		//		strDummy = (ForthStack[--ForthStackIndex]).ToString();	// Value to convert
		//		Dummy1 = ForthStack[--ForthStackIndex];		// Address
		//		Dummy2 = strDummy.Length;
		//		Dummy3 = 0;
		//		while(Dummy2-- > 0)
		//		{
		//			ForthStack[Dummy1++] = strDummy[Dummy3++];
		//		}
		//		ForthStack[Dummy1] = 0;
		// Output: None
		private void _Int2Str(ILGenerator ilgen)
		{
			Label lb1 = ilgen.DefineLabel();
			Label lb2 = ilgen.DefineLabel();
			
			ilgen.Emit(OpCodes.Ldsfld, ForthStack);
			ilgen.Emit(OpCodes.Ldsfld, ForthStackIndex);
			ilgen.Emit(OpCodes.Ldc_I4_1);
			ilgen.Emit(OpCodes.Sub);
			ilgen.Emit(OpCodes.Dup);
			ilgen.Emit(OpCodes.Stsfld, ForthStackIndex);
			ilgen.Emit(OpCodes.Ldelema, typeof(System.Int32));
			ilgen.Emit(OpCodes.Call, typeof(System.Int32).GetMethod("ToString", new Type[] {}));
			ilgen.Emit(OpCodes.Stsfld, strDummy);
			
			ilgen.Emit(OpCodes.Ldsfld, ForthStack);
			ilgen.Emit(OpCodes.Ldsfld, ForthStackIndex);
			ilgen.Emit(OpCodes.Ldc_I4_1);
			ilgen.Emit(OpCodes.Sub);
			ilgen.Emit(OpCodes.Dup);
			ilgen.Emit(OpCodes.Stsfld, ForthStackIndex);
			ilgen.Emit(OpCodes.Ldelem_I4);
			ilgen.Emit(OpCodes.Stsfld, Dummy1);
			ilgen.Emit(OpCodes.Ldsfld, strDummy);
			ilgen.Emit(OpCodes.Callvirt, typeof(string).GetMethod("get_Length"));
			ilgen.Emit(OpCodes.Stsfld, Dummy2);
			ilgen.Emit(OpCodes.Ldc_I4_0);
			ilgen.Emit(OpCodes.Stsfld, Dummy3);
			ilgen.Emit(OpCodes.Br_S, lb1);
			ilgen.MarkLabel(lb2);
			ilgen.Emit(OpCodes.Ldsfld, ForthStack);
			ilgen.Emit(OpCodes.Ldsfld, Dummy1);
			ilgen.Emit(OpCodes.Dup);
			ilgen.Emit(OpCodes.Ldc_I4_1);
			ilgen.Emit(OpCodes.Add);
			ilgen.Emit(OpCodes.Stsfld, Dummy1);
			ilgen.Emit(OpCodes.Ldsfld, strDummy);
			ilgen.Emit(OpCodes.Ldsfld, Dummy3);
			ilgen.Emit(OpCodes.Dup);
			ilgen.Emit(OpCodes.Ldc_I4_1);
			ilgen.Emit(OpCodes.Add);
			ilgen.Emit(OpCodes.Stsfld, Dummy3);
			ilgen.Emit(OpCodes.Callvirt, typeof(string).GetMethod("get_Chars", new Type[] { typeof(int) }));
			ilgen.Emit(OpCodes.Stelem_I4);
			ilgen.MarkLabel(lb1);
			ilgen.Emit(OpCodes.Ldsfld, Dummy2);
			ilgen.Emit(OpCodes.Dup);
			ilgen.Emit(OpCodes.Ldc_I4_1);
			ilgen.Emit(OpCodes.Sub);
			ilgen.Emit(OpCodes.Stsfld, Dummy2);
			ilgen.Emit(OpCodes.Ldc_I4_0);
			ilgen.Emit(OpCodes.Bgt_S, lb2);
			ilgen.Emit(OpCodes.Ldsfld, ForthStack);
			ilgen.Emit(OpCodes.Ldsfld, Dummy1);
			ilgen.Emit(OpCodes.Ldc_I4_0);
			ilgen.Emit(OpCodes.Stelem_I4);
		}
		
		// _Exit	- Exits from the current word
		// Input:  ILGenerator for the method
		// Output: None
		private void _Exit(ILGenerator ilgen)
		{
			ilgen.Emit(OpCodes.Ret);
		}

		// _If - Processes the IF atom
		// Input:  ILGenerator for the method
		// Output: None
		private void _If(ILGenerator ilgen)
		{
			_tagIF sIF = new _tagIF();
			sIF.lbElse = ilgen.DefineLabel();
			sIF.lbEnd = ilgen.DefineLabel();
			sIF.bElse = false;

			ilgen.Emit(OpCodes.Ldsfld, ForthStack);
			ilgen.Emit(OpCodes.Ldsfld, ForthStackIndex);
			ilgen.Emit(OpCodes.Ldc_I4_1);
			ilgen.Emit(OpCodes.Sub);
			ilgen.Emit(OpCodes.Dup);
			ilgen.Emit(OpCodes.Stsfld, ForthStackIndex);
			ilgen.Emit(OpCodes.Ldelem_I4);
			ilgen.Emit(OpCodes.Brfalse, sIF.lbElse);

			IfStack.Push(sIF);
		}

		// _Else - Processes the ELSE atom
		// Input:  ILGenerator for the method
		// Output: None
		private void _Else(ILGenerator ilgen)
		{
			_tagIF sIF = (_tagIF)IfStack.Pop();

			ilgen.Emit(OpCodes.Br, sIF.lbEnd);	// Avoid executing the False branch
			ilgen.MarkLabel(sIF.lbElse);
			sIF.bElse = true;

			IfStack.Push(sIF);
		}

		// _Then - Processes the THEN atom
		// Input:  ILGenerator for the method
		// Output: None
		private void _Then(ILGenerator ilgen)
		{
			_tagIF sIF = (_tagIF)IfStack.Pop();

			ilgen.MarkLabel(sIF.lbEnd);
			if(sIF.bElse == false) ilgen.MarkLabel(sIF.lbElse);
		}

		// _Begin - Processes the BEGIN atom
		// Input:  ILGenerator for the method
		// Output: None
		private void _Begin(ILGenerator ilgen)
		{
			_tagBEGIN sBEGIN = new _tagBEGIN();
			sBEGIN.lbBegin = ilgen.DefineLabel();
			sBEGIN.lbEnd = ilgen.DefineLabel();
			
			ilgen.MarkLabel(sBEGIN.lbBegin);
			BeginStack.Push(sBEGIN);
		}

		// _Until - Processes the UNTIL atom
		// Input:  ILGenerator for the method
		// Output: None
		private void _Until(ILGenerator ilgen)
		{
			_tagBEGIN sBEGIN = (_tagBEGIN)BeginStack.Pop();

			ilgen.Emit(OpCodes.Ldsfld, ForthStack);
			ilgen.Emit(OpCodes.Ldsfld, ForthStackIndex);
			ilgen.Emit(OpCodes.Ldc_I4_1);
			ilgen.Emit(OpCodes.Sub);
			ilgen.Emit(OpCodes.Dup);
			ilgen.Emit(OpCodes.Stsfld, ForthStackIndex);
			ilgen.Emit(OpCodes.Ldelem_I4);
			ilgen.Emit(OpCodes.Brfalse, sBEGIN.lbBegin);
		}

		// _Again - Processes the AGAIN atom
		// Input:  ILGenerator for the method
		// Output: None
		private void _Again(ILGenerator ilgen)
		{
			_tagBEGIN sBEGIN = (_tagBEGIN)BeginStack.Pop();
			ilgen.Emit(OpCodes.Br, sBEGIN.lbBegin);
		}

		// _While - Processes the WHILE atom
		// Input:  ILGenerator for the method
		// Output: None
		private void _While(ILGenerator ilgen)
		{
			_tagBEGIN sBEGIN = (_tagBEGIN)BeginStack.Peek();

			ilgen.Emit(OpCodes.Ldsfld, ForthStack);
			ilgen.Emit(OpCodes.Ldsfld, ForthStackIndex);
			ilgen.Emit(OpCodes.Ldc_I4_1);
			ilgen.Emit(OpCodes.Sub);
			ilgen.Emit(OpCodes.Dup);
			ilgen.Emit(OpCodes.Stsfld, ForthStackIndex);
			ilgen.Emit(OpCodes.Ldelem_I4);
			ilgen.Emit(OpCodes.Brfalse, sBEGIN.lbEnd);
		}

		// _Repeat - Processes the REPEAT atom
		// Input:  ILGenerator for the method
		// Output: None
		private void _Repeat(ILGenerator ilgen)
		{
			_tagBEGIN sBEGIN = (_tagBEGIN)BeginStack.Pop();
			ilgen.Emit(OpCodes.Br, sBEGIN.lbBegin);
			ilgen.MarkLabel(sBEGIN.lbEnd);
		}

		// _Case - Processes the CASE atom
		// Input:  ILGenerator for the method
		// Output: None
		private void _Case(ILGenerator ilgen)
		{
			Label lbEndCase = ilgen.DefineLabel();
			CaseStack.Push(lbEndCase);
		}

		// _Of - Processes the OF atom
		// Input:  ILGenerator for the method
		// Output: None
		private void _Of(ILGenerator ilgen)
		{
			Label lbEndOf = ilgen.DefineLabel();
			ilgen.Emit(OpCodes.Ldsfld, ForthStack);
			ilgen.Emit(OpCodes.Ldsfld, ForthStackIndex);
			ilgen.Emit(OpCodes.Ldc_I4_2);
			ilgen.Emit(OpCodes.Sub);
			ilgen.Emit(OpCodes.Ldelem_I4);
			ilgen.Emit(OpCodes.Ldsfld, ForthStack);
			ilgen.Emit(OpCodes.Ldsfld, ForthStackIndex);
			ilgen.Emit(OpCodes.Ldc_I4_1);
			ilgen.Emit(OpCodes.Sub);
			ilgen.Emit(OpCodes.Dup);
			ilgen.Emit(OpCodes.Stsfld, ForthStackIndex);
			ilgen.Emit(OpCodes.Ldelem_I4);
			ilgen.Emit(OpCodes.Bne_Un, lbEndOf);
			CaseStack.Push(lbEndOf);
		}

		// _EndOf - Processes the OF atom
		// Input:  ILGenerator for the method
		// Output: None
		private void _EndOf(ILGenerator ilgen)
		{
			Label lbEndOf = (Label)CaseStack.Pop();
			Label lbEndCase = (Label)CaseStack.Pop();
			ilgen.Emit(OpCodes.Br, lbEndCase);
			ilgen.MarkLabel(lbEndOf);
			CaseStack.Push(lbEndCase);
		}

		// _EndCase - Processes the ENDCASE atom
		// Input:  ILGenerator for the method
		// Output: None
		private void _EndCase(ILGenerator ilgen)
		{
			Label lbEndCase = (Label)CaseStack.Pop();
			ilgen.MarkLabel(lbEndCase);
			_Drop(ilgen);
		}

		// _Do - Processes the DO atom
		// Input:  ILGenerator for the method
		// Output: None
		private void _Do(ILGenerator ilgen)
		{
			_tagDO sDO = new _tagDO();
			sDO.lbDo = ilgen.DefineLabel();
			sDO.lbLoop = ilgen.DefineLabel();
			_Swap(ilgen);
			_ToR(ilgen);
			_ToR(ilgen);
			ilgen.MarkLabel(sDO.lbDo);
			DoStack.Push(sDO);
		}

		// _Leave - Processes the LEAVE atom
		// Input:  ILGenerator for the method
		// Output: None
		private void _Leave(ILGenerator ilgen)
		{
			_tagDO sDO = (_tagDO)DoStack.Peek();
			ilgen.Emit(OpCodes.Br, sDO.lbLoop);
		}

		// _Loop - Processes the LOOP atom
		// Input:  ILGenerator for the method
		// Output: None
		private void _Loop(ILGenerator ilgen)
		{
			_tagDO sDO = (_tagDO)DoStack.Peek();
			ilgen.Emit(OpCodes.Ldsfld, ReturnStack);
			ilgen.Emit(OpCodes.Ldsfld, ReturnStackIndex);
			ilgen.Emit(OpCodes.Ldc_I4_1);
			ilgen.Emit(OpCodes.Sub);
			ilgen.Emit(OpCodes.Ldelem_I4);
			ilgen.Emit(OpCodes.Stsfld, DoLoopDummy);
			ilgen.Emit(OpCodes.Ldsfld, DoLoopDummy);
			ilgen.Emit(OpCodes.Ldsfld, ReturnStack);
			ilgen.Emit(OpCodes.Ldsfld, ReturnStackIndex);
			ilgen.Emit(OpCodes.Ldc_I4_2);
			ilgen.Emit(OpCodes.Sub);
			ilgen.Emit(OpCodes.Ldelem_I4);
			ilgen.Emit(OpCodes.Bge_S, sDO.lbLoop);
			ilgen.Emit(OpCodes.Ldsfld, ReturnStack);
			ilgen.Emit(OpCodes.Ldsfld, ReturnStackIndex);
			ilgen.Emit(OpCodes.Ldc_I4_1);
			ilgen.Emit(OpCodes.Sub);
			ilgen.Emit(OpCodes.Ldsfld, DoLoopDummy);
			ilgen.Emit(OpCodes.Ldc_I4_1);
			ilgen.Emit(OpCodes.Add);
			ilgen.Emit(OpCodes.Stelem_I4);
			ilgen.Emit(OpCodes.Br, sDO.lbDo);
			ilgen.MarkLabel(sDO.lbLoop);
			DoStack.Pop();
		}

		// _PlusLoop - Processes the +LOOP atom
		// Input:  ILGenerator for the method
		// Output: None
		private void _PlusLoop(ILGenerator ilgen)
		{
			_tagDO sDO = (_tagDO)DoStack.Peek();
			ilgen.Emit(OpCodes.Ldsfld, ReturnStack);
			ilgen.Emit(OpCodes.Ldsfld, ReturnStackIndex);
			ilgen.Emit(OpCodes.Ldc_I4_1);
			ilgen.Emit(OpCodes.Sub);
			ilgen.Emit(OpCodes.Ldelem_I4);
			ilgen.Emit(OpCodes.Stsfld, DoLoopDummy);
			ilgen.Emit(OpCodes.Ldsfld, DoLoopDummy);
			ilgen.Emit(OpCodes.Ldsfld, ReturnStack);
			ilgen.Emit(OpCodes.Ldsfld, ReturnStackIndex);
			ilgen.Emit(OpCodes.Ldc_I4_2);
			ilgen.Emit(OpCodes.Sub);
			ilgen.Emit(OpCodes.Ldelem_I4);
			ilgen.Emit(OpCodes.Bge_S, sDO.lbLoop);
			ilgen.Emit(OpCodes.Ldsfld, ReturnStack);
			ilgen.Emit(OpCodes.Ldsfld, ReturnStackIndex);
			ilgen.Emit(OpCodes.Ldc_I4_1);
			ilgen.Emit(OpCodes.Sub);
			ilgen.Emit(OpCodes.Ldsfld, DoLoopDummy);
			ilgen.Emit(OpCodes.Ldsfld, ForthStack);
			ilgen.Emit(OpCodes.Ldsfld, ForthStackIndex);
			ilgen.Emit(OpCodes.Ldc_I4_1);
			ilgen.Emit(OpCodes.Sub);
			ilgen.Emit(OpCodes.Dup);
			ilgen.Emit(OpCodes.Stsfld, ForthStackIndex);
			ilgen.Emit(OpCodes.Ldelem_I4);
			ilgen.Emit(OpCodes.Add);
			ilgen.Emit(OpCodes.Stelem_I4);
			ilgen.Emit(OpCodes.Br, sDO.lbDo);
			ilgen.MarkLabel(sDO.lbLoop);
			DoStack.Pop();
		}

		// CallExternalMethod - Calls a method from a class in an external file
		// Input:  ILGenerator for the method
		//			FileName - the name of the file containing the class
		//			ClassName - the name of the class
		//			MethodName - the name of the method to call
		// Output: None
		private void CallExternalMethod(ILGenerator ilgen, string FileName, string ClassName, string MethodName)
		{
			if(!bExtCallerDefined)
			{
				// The runtime does not contain the ExternalCaller function, we add it here
				// Function ExternalCaller
				Type[] parameters = { typeof(String) /* FileName */, typeof(String) /* ClassName */, typeof(String) /* MethodName */ };
				extCaller = ForthEngineClass.DefineMethod("ExternalCaller", MethodAttributes.Private|MethodAttributes.Static, typeof(void), parameters);
				// Field calAssembly
				FieldBuilder calAssembly = ForthEngineClass.DefineField("CalleeAssembly", typeof(System.Reflection.Assembly), FieldAttributes.Private|FieldAttributes.Static);
				// Field calType
				FieldBuilder calType = ForthEngineClass.DefineField("CalleeType", typeof(System.Type), FieldAttributes.Private|FieldAttributes.Static);
				// Field calMethodInfo
				FieldBuilder calMethodInfo = ForthEngineClass.DefineField("CalleeMethodInfo", typeof(System.Reflection.MethodInfo), FieldAttributes.Private|FieldAttributes.Static);
				// Field calInstance
				FieldBuilder calInstance = ForthEngineClass.DefineField("CalleeInstance", typeof(System.Object), FieldAttributes.Private|FieldAttributes.Static);

				ILGenerator extCallerILGen = extCaller.GetILGenerator();

				// Define a few local labels
				Label lb1 = extCallerILGen.DefineLabel();
				Label lb2 = extCallerILGen.DefineLabel();
				Label lb3 = extCallerILGen.DefineLabel();
				Label lb4 = extCallerILGen.DefineLabel();

				extCallerILGen.BeginExceptionBlock();

				extCallerILGen.Emit(OpCodes.Ldarg_0);
				extCallerILGen.Emit(OpCodes.Call, typeof(System.Reflection.Assembly).GetMethod("LoadFrom", new Type[] { typeof(String) }));
				extCallerILGen.Emit(OpCodes.Stsfld, calAssembly);
				extCallerILGen.Emit(OpCodes.Ldsfld, calAssembly);
				extCallerILGen.Emit(OpCodes.Ldarg_1);
				extCallerILGen.Emit(OpCodes.Ldc_I4_0);
				extCallerILGen.Emit(OpCodes.Ldc_I4_1);
				extCallerILGen.Emit(OpCodes.Callvirt, typeof(System.Reflection.Assembly).GetMethod("GetType", new Type[] { typeof(String), typeof(bool), typeof(bool) }));
				extCallerILGen.Emit(OpCodes.Stsfld, calType);
				extCallerILGen.Emit(OpCodes.Ldsfld, calType);
				extCallerILGen.Emit(OpCodes.Brtrue_S, lb1);
				extCallerILGen.EmitWriteLine("\n\rRUNTIME ERROR: Could not load library.");
				extCallerILGen.Emit(OpCodes.Br_S, lb2);
				extCallerILGen.MarkLabel(lb1);
				extCallerILGen.Emit(OpCodes.Ldsfld, calType);
				extCallerILGen.Emit(OpCodes.Ldarg_2);
				extCallerILGen.Emit(OpCodes.Callvirt, typeof(System.Type).GetMethod("GetMethod", new Type[] { typeof(String) }));
				extCallerILGen.Emit(OpCodes.Stsfld, calMethodInfo);
				extCallerILGen.Emit(OpCodes.Ldsfld, calMethodInfo);
				extCallerILGen.Emit(OpCodes.Brtrue_S, lb3);
				extCallerILGen.EmitWriteLine("\n\rRUNTIME ERROR: Could not call external method.");
				extCallerILGen.Emit(OpCodes.Br_S, lb2);
				extCallerILGen.MarkLabel(lb3);
				extCallerILGen.Emit(OpCodes.Ldnull);
				extCallerILGen.Emit(OpCodes.Stsfld, calInstance);
				extCallerILGen.Emit(OpCodes.Ldsfld, calMethodInfo);
				extCallerILGen.Emit(OpCodes.Callvirt, typeof(System.Reflection.MethodBase).GetMethod("get_IsStatic", new Type[] {}));
				extCallerILGen.Emit(OpCodes.Brtrue_S, lb4);
				extCallerILGen.Emit(OpCodes.Ldsfld, calType);
				extCallerILGen.Emit(OpCodes.Call, typeof(System.Activator).GetMethod("CreateInstance", new Type[] { typeof(System.Type) }));
				extCallerILGen.Emit(OpCodes.Stsfld, calInstance);
				extCallerILGen.MarkLabel(lb4);
				extCallerILGen.Emit(OpCodes.Ldsfld, calMethodInfo);
				extCallerILGen.Emit(OpCodes.Ldnull);
				extCallerILGen.Emit(OpCodes.Ldnull);
				extCallerILGen.Emit(OpCodes.Callvirt, typeof(System.Reflection.MethodBase).GetMethod("Invoke", new Type[] {	typeof(Object), typeof(Object[]) }));
				extCallerILGen.Emit(OpCodes.Stsfld, calInstance);
				extCallerILGen.BeginCatchBlock(typeof(System.IO.FileNotFoundException));
				extCallerILGen.Emit(OpCodes.Pop);
				extCallerILGen.ThrowException(typeof(System.IO.FileNotFoundException));
				extCallerILGen.EndExceptionBlock();
				extCallerILGen.MarkLabel(lb2);
				extCallerILGen.Emit(OpCodes.Ret);

				bExtCallerDefined = true;
			}

			// ExternalCaller method is already defined in runtime, we just call it
			ilgen.Emit(OpCodes.Ldstr, FileName);
			ilgen.Emit(OpCodes.Ldstr, ClassName);
			ilgen.Emit(OpCodes.Ldstr, MethodName);
			ilgen.Emit(OpCodes.Call, extCaller);
		}
	}

}
