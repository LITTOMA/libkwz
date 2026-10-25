// Read-only survey of KWZ signing code and references.
// @category KWZ
import ghidra.app.script.GhidraScript;
import ghidra.app.decompiler.*;
import ghidra.program.model.address.*;
import ghidra.program.model.listing.*;
import ghidra.program.model.symbol.*;
import java.io.*;
import java.util.*;
import java.math.BigInteger;

public class KwzSignatureSurvey extends GhidraScript {
    public void run() throws Exception {
        String[] args=getScriptArgs();
        // The existing raw-binary project was imported at zero and has no functions.
        // Rebase and analyze only in memory; run with -readOnly to discard these changes.
        if (currentProgram.getImageBase().getOffset()==0) currentProgram.setImageBase(toAddr(0x100000),true);
        currentProgram.getProgramContext().setValue(currentProgram.getRegister("TMode"),toAddr(0x100000),toAddr(0x37304f),BigInteger.ZERO);
        long[][] ranges={{0x322300,0x322500},{0x322b00,0x323000},{0x202500,0x202700}};
        for(long[] range:ranges) for(long p=range[0];p<range[1];p+=4) {
            int word=currentProgram.getMemory().getInt(toAddr(p));
            if((word & 0xffff4000)==0xe92d4000 || word==0xe52de004) {
                disassemble(toAddr(p));
                if(getFunctionAt(toAddr(p))==null) createFunction(toAddr(p),null);
            }
        }
        analyzeChanges(currentProgram);
        try(PrintWriter out=new PrintWriter(args[0], "UTF-8")) {
            out.println("PROGRAM " + currentProgram.getName()+" imageBase="+currentProgram.getImageBase()+" language="+currentProgram.getLanguageID());
            out.println("FUNCTIONS "+currentProgram.getFunctionManager().getFunctionCount());
            for(var block: currentProgram.getMemory().getBlocks()) out.println("BLOCK "+block.getName()+" "+block.getStart()+".."+block.getEnd());
            var selected=new LinkedHashSet<Function>();
            for(int i=1;i<args.length;i++) {
                Address target=toAddr(args[i]);
                out.println("TARGET "+target);
                out.println("WORD "+Integer.toHexString(currentProgram.getMemory().getInt(target))+" INSTRUCTION "+getInstructionAt(target));
                Function own=getFunctionContaining(target);
                if(own!=null) selected.add(own);
                for(var ref:getReferencesTo(target)) {
                    Function f=getFunctionContaining(ref.getFromAddress());
                    out.println("REF "+ref+" FUNCTION "+f);
                    if(f!=null) selected.add(f);
                }
            }
            var funcs=currentProgram.getFunctionManager().getFunctions(true);
            while(funcs.hasNext()) {
                Function f=funcs.next();
                if(f.getName().toLowerCase().matches(".*(rsa|sha256|sign|kwz).*")) out.println("NAMED "+f.getEntryPoint()+" "+f.getName());
            }
            var decompiler=new DecompInterface();
            try {
                decompiler.openProgram(currentProgram);
                for(Function f:selected) {
                    out.println("\nFUNCTION "+f.getEntryPoint()+" "+f.getName());
                    for(var ref:getReferencesTo(f.getEntryPoint())) out.println("CALLER "+ref.getFromAddress()+" "+getFunctionContaining(ref.getFromAddress()));
                    var result=decompiler.decompileFunction(f,30,monitor);
                    out.println(result.decompileCompleted()?result.getDecompiledFunction().getC():result.getErrorMessage());
                }
            } finally {decompiler.dispose();}
        }
    }
}
