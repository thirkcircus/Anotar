﻿using System;
using System.Linq;
using Mono.Cecil;
using Mono.Cecil.Cil;
using Mono.Cecil.Rocks;

public partial class ModuleWeaver
{
    void ProcessType(TypeDefinition type)
    {
        var fieldDefinition = type.Fields.FirstOrDefault(x => x.IsStatic && x.FieldType.FullName == LoggerType.FullName);
        Action foundAction;
        if (fieldDefinition == null)
        {
            fieldDefinition = new FieldDefinition("AnotarLogger", FieldAttributes.Static | FieldAttributes.Private, LoggerType)
                {
                    DeclaringType = type,
                    IsStatic = true,
                };
            foundAction = () => InjectField(type, fieldDefinition);
        }
        else
        {
            foundAction = () => { };
        }
        var fieldReference = fieldDefinition.GetGeneric();
        var foundUsage = false;
        foreach (var method in type.Methods)
        {
            //skip for abstract and delegates
            if (!method.HasBody)
            {
                continue;
            }

            var onExceptionProcessor = new OnExceptionProcessor
                {
                    Method = method,
                    Field = fieldReference,
                    FoundUsageInType = () => foundUsage = true,
                    ModuleWeaver = this
                };
            onExceptionProcessor.Process();

            var logForwardingProcessor = new LogForwardingProcessor
                {
					FoundUsageInType = () => foundUsage = true,
                    Method = method,
					ModuleWeaver = this,
                    Field = fieldReference,
                };
            logForwardingProcessor.ProcessMethod();

        }
        if (foundUsage)
        {
            foundAction();
        }
    }

    void InjectField(TypeDefinition type, FieldDefinition fieldDefinition)
    {
        var staticConstructor = type.GetStaticConstructor();
        staticConstructor.Body.SimplifyMacros();
        var instructions = staticConstructor.Body.Instructions;

        TypeReference declaringType;
        if (type.IsCompilerGenerated() && type.IsNested)
        {
            declaringType = type.DeclaringType.GetGeneric();
        }
        else
        {
            declaringType = type.GetGeneric();
        }

        instructions.Insert(0, Instruction.Create(OpCodes.Ldtoken, declaringType));
        instructions.Insert(1, Instruction.Create(OpCodes.Call, getTypeFromHandle));
        instructions.Insert(2, Instruction.Create(OpCodes.Call, buildLoggerMethod));
        instructions.Insert(3, Instruction.Create(OpCodes.Stsfld, fieldDefinition.GetGeneric()));
        type.Fields.Add(fieldDefinition);
        staticConstructor.Body.OptimizeMacros();
    }


}