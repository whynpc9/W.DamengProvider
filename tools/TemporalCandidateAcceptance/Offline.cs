using System.Text.Json;

internal static partial class Program
{
    // Calls the same live policy before Configure or any DAMENG environment access.
    static int Offline(string[] args)
    {
        if(args.Length!=9)return 2;Output=args[7];int exit=1;var checks=new List<string>();
        try
        {
            Identity=LoadStrict(args[1]);Profile=args[3];Repo=args[5];
            CheckExactCandidate(args[1],args[8]);
            Report["identity_sha256"]=Hash(File.ReadAllBytes(args[1]));Report["identities"]=CheckIdentities(args[2]);CheckSourceBinding(args[8]);CheckTools(args[8]);
            Need(Environment.Version.Major==10,"offline_runtime");
            var owner=new Ownership(1,new string('A',32),"TPN_"+new string('A',24),Profile,ExpectedVersion,new string('b',64),new string('c',64),true,true,"OWN_"+new string('A',24),"confirmed");
            void Check(string id,Action action){action();checks.Add(id);}
            void Reject(Action action) { bool failed=false;try {action();}catch(InvalidOperationException){failed=true;}Need(failed,"offline_required_rejection"); }
            Check("owner_valid",()=>ValidateOwnership(owner,Profile,ExpectedVersion,new string('b',64),new string('c',64)));
            Check("owner_bad_nonce",()=>Reject(()=>ValidateOwnership(owner with{Nonce=new string('A',34)},Profile,ExpectedVersion,new string('b',64),new string('c',64))));
            Check("marker_exact",()=>Need(MarkerMatches(owner,"(('"+owner.Nonce+"'))")&&!MarkerMatches(owner,"'"+owner.Nonce+"X'"),"offline_marker"));
            Check("create_collision",()=>{var f=new FakeOwner{Present=true};Reject(()=>CreateWithLifecycle(owner with{CreationState="reserved"},f,_=>{}));Need(f.Creates==0&&f.Drops==0,"offline_collision_side_effect");});
            Check("create_states",()=>{var f=new FakeOwner();var states=new List<string>();CreateWithLifecycle(owner with{CreationState="reserved"},f,o=>states.Add(o.CreationState));Need(states.SequenceEqual(new[]{"reserved","create_inflight","confirmed"})&&f.Creates==1,"offline_create_states");});
            Check("cleanup_exact",()=>{var f=new FakeOwner{Present=true,Default="'"+owner.Nonce+"'"};CleanupWithOwnership(owner,f,Profile,ExpectedVersion,new string('b',64),new string('c',64));Need(f.Drops==1&&f.FreshChecks==1,"offline_cleanup_exact");});
            Check("cleanup_foreign",()=>{var f=new FakeOwner{Present=true,Default="'foreign'"};Reject(()=>CleanupWithOwnership(owner,f,Profile,ExpectedVersion,new string('b',64),new string('c',64)));Need(f.Drops==0,"offline_foreign_never_drop");});
            Check("cleanup_known_failure",()=>{var f=new FakeOwner{Present=true,Default="'"+owner.Nonce+"'"};Reject(()=>CleanupWithOwnership(owner with{CreationState="known_create_failure"},f,Profile,ExpectedVersion,new string('b',64),new string('c',64)));Need(f.Drops==0,"offline_known_never_drop");});
            Check("cleanup_fresh_failure",()=>{var f=new FakeOwner{FreshPresent=true};Reject(()=>CleanupWithOwnership(owner,f,Profile,ExpectedVersion,new string('b',64),new string('c',64)));});
            Check("duplicate_json",()=>Reject(()=>ParseStrict("{\"a\":1,\"a\":2}")));
            Check("physical_missing_closed",()=>Reject(()=>CapturePhysicalProfile(new object(),User,User)));
            Check("safe_exception",()=>Need(Safe(new InvalidOperationException("do_not_export_this_message")).Type=="System.InvalidOperationException"&&Safe(new InvalidOperationException("do_not_export_this_message")).AssertionCode is null&&Safe(new ToolAssertionException(ToolAssertionCode.FreshIdReused)).AssertionCode=="FreshIdReused","offline_safe_type"));
            Check("server_null_sync_getter",()=>{
                var f=new FakeServerProjection(true);var observation=ObserveServerProjection(true,1,false,f.IsNull,f.IsNullAsync,f.Text).GetAwaiter().GetResult();
                Need(f.SyncCalls==1&&f.AsyncCalls==0&&f.TextCalls==0&&observation.IsNull&&observation.Text is null&&observation.Receipt==new ServerNullReceipt(true,"IsDBNull",1,true,"server_projection"),"offline_server_null_sync");
            });
            Check("server_null_async_getter",()=>{
                var f=new FakeServerProjection(true);var observation=ObserveServerProjection(true,6,true,f.IsNull,f.IsNullAsync,f.Text).GetAwaiter().GetResult();
                Need(f.SyncCalls==0&&f.AsyncCalls==1&&f.TextCalls==0&&observation.IsNull&&observation.Receipt==new ServerNullReceipt(true,"IsDBNullAsync",6,true,"server_projection"),"offline_server_null_async");
            });
            Check("server_null_conflict_refused",()=>{
                var f=new FakeServerProjection(false);Reject(()=>ObserveServerProjection(true,1,true,f.IsNull,f.IsNullAsync,f.Text).GetAwaiter().GetResult());
                Need(f.AsyncCalls==1&&f.SyncCalls==0&&f.TextCalls==0,"offline_server_null_conflict_call");
            });
            Check("server_nonnull_projection_getters",()=>{
                var f=new FakeServerProjection(false);var observation=ObserveServerProjection(false,1,false,f.IsNull,f.IsNullAsync,f.Text).GetAwaiter().GetResult();
                Need(f.SyncCalls==1&&f.AsyncCalls==0&&f.TextCalls==1&&!observation.IsNull&&observation.Text=="2026-07-23"&&!observation.Receipt.IsNull,"offline_server_nonnull_call");
            });
            Frame Allocation()=>new("offline",3,3,0,0);
            Frame Prepare()=>new("offline",5,5,0,32);
            Frame Execute(short code=13)=>new("offline",code,code,0,32);
            Check("frame_classification",()=>Need(ClassifyProtocolFrame(3)==ProtocolFrameKind.StatementAllocation&&ClassifyProtocolFrame(5)==ProtocolFrameKind.PrepareOrDirectSql&&ClassifyProtocolFrame(6)==ProtocolFrameKind.PreparedExecute&&ClassifyProtocolFrame(13)==ProtocolFrameKind.PreparedExecute&&ClassifyProtocolFrame(44)==ProtocolFrameKind.Other,"offline_frame_kinds"));
            Check("prepared_collection_exact",()=>{
                var frames=new List<Frame>();Need(CollectPreparedFrame(frames,Allocation())&&CollectPreparedFrame(frames,Prepare())&&CollectPreparedFrame(frames,Execute(6))&&CollectPreparedFrame(frames,Execute())&&!CollectPreparedFrame(frames,new("offline",44,44,0,32))&&frames.Select(f=>f.RequestOpcode).SequenceEqual(new short[]{3,5,6,13}),"offline_prepared_frame_collection");
            });
            Check("prepared_execute_control",()=>{
                var frames=new List<Frame>();CollectPreparedFrame(frames,Execute());var window=ObservePreparedWindow(frames,new int?[]{7},7,false);
                Need(window.AllocationCount==0&&window.PrepareOrDirectCount==0&&window.ReprepareCount==0&&window.CurrentNoReprepare&&window.BusinessFrames.SequenceEqual(frames),"offline_prepared_control");
            });
            Check("prepared_extra5_refused",()=>{
                var frames=new List<Frame>();CollectPreparedFrame(frames,Prepare());CollectPreparedFrame(frames,Execute());var window=ObservePreparedWindow(frames,new int?[]{7,7},7,false);
                Need(window.AllocationCount==0&&window.PrepareOrDirectCount==1&&window.ReprepareCount==1&&!window.CurrentNoReprepare&&window.BusinessFrames.SequenceEqual(new[]{frames[1]}),"offline_extra_prepare5");
            });
            Check("prepared_allocation3_refused",()=>{
                var frames=new List<Frame>();CollectPreparedFrame(frames,Allocation());var window=ObservePreparedWindow(frames,new int?[]{7},7,false);
                Need(window.AllocationCount==1&&window.PrepareOrDirectCount==0&&window.ReprepareCount==0&&!window.CurrentNoReprepare&&window.BusinessFrames.Length==0,"offline_allocation_not_prepare");
            });
            Check("direct5_error_response",()=>{
                var frames=new List<Frame>();CollectDirectFrame(frames,Allocation());var error=new Frame("offline",5,5,-6118,64);CollectDirectFrame(frames,error);CollectDirectFrame(frames,Execute());
                var response=ObserveDirectResponse(frames);var serialized=JsonSerializer.SerializeToElement(WireResponse(response),Json);
                Need(frames.Count==2&&response==error&&serialized.GetProperty("request_opcode").GetInt32()==5&&serialized.GetProperty("sql_code").GetInt32()==-6118,"offline_direct_business_error");
            });
            Check("direct_allocation_response_refused",()=>{
                var frames=new List<Frame>();CollectDirectFrame(frames,Allocation());Reject(()=>ObserveDirectResponse(frames));
            });
            Check("direct_ambiguous_response_refused",()=>{
                var frames=new List<Frame>();CollectDirectFrame(frames,Prepare());CollectDirectFrame(frames,new("offline",5,5,-6118,64));Reject(()=>ObserveDirectResponse(frames));
            });
            Dictionary<string,object?> RejectionStage(System.Data.ConnectionState state)=>new(){["error"]=new ErrorInfo("System.OverflowException",null,"user_rejection",false,null,null,null,null),["target_receipt"]=new Dictionary<string,object?>{["state_before"]="Open",["state_after"]=state.ToString(),["api"]="ExecuteNonQueryAsync",["parameter_count"]=1,["business_send_count"]=0,["execute_returned"]=false}};
            Dictionary<string,object?> RecoveryOf(Dictionary<string,object?> stage)=>(Dictionary<string,object?>)stage["recovery"]!;
            Check("recovery_open_control",()=>{
                var stage=RejectionStage(System.Data.ConnectionState.Open);var adapter=new FakeRecovery(System.Data.ConnectionState.Open);Recover(stage,adapter,true).GetAwaiter().GetResult();var receipt=RecoveryOf(stage);
                Need(receipt["path"] is "same_open"&&receipt["completed"] is true&&receipt["state_after"] is "Open"&&adapter.OldProbes==1&&adapter.CommandDisposes==0&&adapter.ConnectionDisposes==0&&adapter.FreshVerifications==1,"offline_open_recovery");
            });
            Check("recovery_broken_control",()=>{
                var stage=RejectionStage(System.Data.ConnectionState.Broken);var adapter=new FakeRecovery(System.Data.ConnectionState.Broken);Recover(stage,adapter,true).GetAwaiter().GetResult();var receipt=RecoveryOf(stage);
                Need(receipt["path"] is "retired_connection"&&receipt["completed"] is true&&receipt["state_after"] is "Closed"&&adapter.SentCount==0&&adapter.CommandDisposes==1&&adapter.ConnectionDisposes==1&&adapter.FreshVerifications==1,"offline_broken_recovery");
            });
            Check("recovery_closed_control",()=>{
                var stage=RejectionStage(System.Data.ConnectionState.Closed);var adapter=new FakeRecovery(System.Data.ConnectionState.Closed);Recover(stage,adapter,true).GetAwaiter().GetResult();Need(RecoveryOf(stage)["completed"] is true&&adapter.OldProbes==1&&adapter.SentCount==0,"offline_closed_recovery");
            });
            Check("recovery_unapproved_state_rejected",()=>{
                var stage=RejectionStage(System.Data.ConnectionState.Connecting);var adapter=new FakeRecovery(System.Data.ConnectionState.Connecting);Reject(()=>Recover(stage,adapter,true).GetAwaiter().GetResult());Need(RecoveryOf(stage)["attempted"] is true&&RecoveryOf(stage)["completed"] is false&&adapter.OldProbes==0,"offline_unapproved_recovery");
            });
            Check("recovery_old_send_rejected",()=>{
                var stage=RejectionStage(System.Data.ConnectionState.Broken);var adapter=new FakeRecovery(System.Data.ConnectionState.Broken){SendOld=true};Reject(()=>Recover(stage,adapter,true).GetAwaiter().GetResult());Need(adapter.CommandDisposes==0&&RecoveryOf(stage)["failed_step"] is "old_connection_probe","offline_old_probe_send");
            });
            Check("recovery_wrong_probe_error_rejected",()=>{
                var stage=RejectionStage(System.Data.ConnectionState.Closed);var adapter=new FakeRecovery(System.Data.ConnectionState.Closed){WrongOldError=true};Reject(()=>Recover(stage,adapter,true).GetAwaiter().GetResult());Need(adapter.FreshOpens==0&&RecoveryOf(stage)["failed_step"] is "old_connection_probe","offline_old_probe_error");
            });
            Check("recovery_fresh_id_reuse_rejected",()=>{
                var stage=RejectionStage(System.Data.ConnectionState.Broken);var adapter=new FakeRecovery(System.Data.ConnectionState.Broken){ReuseFreshId=true};Reject(()=>Recover(stage,adapter,true).GetAwaiter().GetResult());Need(RecoveryOf(stage)["failed_step"] is "fresh_connection_open","offline_reused_fresh");
            });
            Check("recovery_fresh_scalar_rejected",()=>{
                var stage=RejectionStage(System.Data.ConnectionState.Open);var adapter=new FakeRecovery(System.Data.ConnectionState.Open){FreshScalar=2};Reject(()=>Recover(stage,adapter,true).GetAwaiter().GetResult());Need(adapter.FreshVerifications==0&&RecoveryOf(stage)["failed_step"] is "fresh_connection_probe","offline_fresh_scalar");
            });
            Check("recovery_midstep_retained",()=>{
                var stage=RejectionStage(System.Data.ConnectionState.Open);var adapter=new FakeRecovery(System.Data.ConnectionState.Open){VerifyFailure=true};Reject(()=>Recover(stage,adapter,true).GetAwaiter().GetResult());var receipt=RecoveryOf(stage);var steps=(List<Dictionary<string,object?>>)receipt["steps"]!;
                Need(receipt["completed"] is false&&receipt["failed_step"] is "fresh_row_verify"&&steps.Any(v=>v["step_id"] is "fresh_row_verify"&&v["status"] is "failed")&&adapter.FreshDisposes==1,"offline_partial_recovery");
            });
            Check("recovery_server_cannot_retire",()=>{
                var stage=RejectionStage(System.Data.ConnectionState.Broken);var adapter=new FakeRecovery(System.Data.ConnectionState.Broken);Reject(()=>Recover(stage,adapter,false).GetAwaiter().GetResult());Need(adapter.OldProbes==0,"offline_server_no_retire_exception");
            });
            Check("recovery_target_business_sent_rejected",()=>{
                var stage=RejectionStage(System.Data.ConnectionState.Open);((Dictionary<string,object?>)stage["target_receipt"]!)["business_send_count"]=1;var adapter=new FakeRecovery(System.Data.ConnectionState.Open);Reject(()=>Recover(stage,adapter,true).GetAwaiter().GetResult());Need(adapter.OldProbes==0,"offline_target_business_sent");
                var sync=RejectionStage(System.Data.ConnectionState.Open);((Dictionary<string,object?>)sync["target_receipt"]!)["api"]="ExecuteNonQuery";var syncAdapter=new FakeRecovery(System.Data.ConnectionState.Open);Reject(()=>Recover(sync,syncAdapter,true).GetAwaiter().GetResult());Need(syncAdapter.OldProbes==0,"offline_target_sync_unobservable");
            });
            Check("recovery_independent_stage_origin",()=>{
                var previous=new Dictionary<string,object?>{["operation_connection_id"]="old",["recovery"]=new Dictionary<string,object?>{["path"]="retired_connection"}};
                Need(RequiresNewIndependentConnection(previous,System.Data.ConnectionState.Closed,"old"),"offline_next_retired");previous["recovery"]=new Dictionary<string,object?>{["path"]="same_open"};Need(!RequiresNewIndependentConnection(previous,System.Data.ConnectionState.Open,"old"),"offline_next_open");Reject(()=>RequiresNewIndependentConnection(previous,System.Data.ConnectionState.Open,"different"));
            });
            ReadTrace NewReadTrace(Dictionary<string,object?> sink)=>new(sink,"W","offline-read",1,0,"I");
            Metadata IntervalMetadata()=>new(true,21,24,1686,0,0,1686,"captured_server_source","ResultColumn",21,24,0,0,"ordinary_packed",false,"result_column");
            Check("read_partial_raw_guard_retained",()=>{
                var sink=new Dictionary<string,object?>{["stage_id"]="write"};var trace=NewReadTrace(sink);trace.Step(ReadStepId.Metadata,IntervalMetadata,v=>v);trace.Step(ReadStepId.Cell,()=>new{observable=true,payload_length=23,payload_sha256=Hash(new byte[23]),is_null=false},v=>v);
                try{trace.Step(ReadStepId.Raw,()=>GuardRawValue(true,IntervalMetadata(),new byte[23]),v=>v);}catch(InvalidOperationException e){trace.Failed(e);}
                Need(((List<object>)sink["read_attempts"]!).Count==1&&trace.Attempt["completed"] is false&&trace.Attempt["failed_step"] is "Raw"&&trace.Steps[1]["status"] is "completed"&&((ErrorInfo)trace.Attempt["error"]!).AssertionCode=="ReadRawWidthInvalid","offline_partial_raw");
            });
            Check("read_partial_value_guard_retained",()=>{
                var trace=NewReadTrace(new());trace.StepAsync(ReadStepId.GenericGetter,()=>Task.FromResult<object?>(TimeSpan.Zero),v=>Observe(()=>v)).GetAwaiter().GetResult();var text=trace.Step(ReadStepId.ValueStringGet,()=>"0 00:00:00.000000 ",v=>DiagnosticText(v));
                try{trace.Step(ReadStepId.ValueTextGuard,()=>GuardReadText(text,"I",false,IntervalMetadata()));}catch(InvalidOperationException e){trace.Failed(e);}
                Need(trace.Attempt["failed_step"] is "ValueTextGuard"&&((ErrorInfo)trace.Attempt["error"]!).AssertionCode=="ReadValueTextGuardFailed"&&trace.Steps[0]["status"] is "completed","offline_partial_value_guard");
            });
            Check("read_partial_server_guard_retained",()=>{
                var trace=NewReadTrace(new());trace.Step(ReadStepId.ValueTextGuard,()=>GuardReadText("INTERVAL '000000000 00:00:00.000000' DAY(9) TO SECOND(6)","I",false,IntervalMetadata()));var text=trace.Step(ReadStepId.ServerStringGet,()=>"0 00:00:00.000000 ",v=>DiagnosticText(v));
                try{trace.Step(ReadStepId.ServerTextGuard,()=>GuardReadText(text,"I",true,IntervalMetadata()));}catch(InvalidOperationException e){trace.Failed(e);}
                Need(trace.Attempt["failed_step"] is "ServerTextGuard"&&((ErrorInfo)trace.Attempt["error"]!).AssertionCode=="ReadServerTextGuardFailed"&&trace.Steps[0]["status"] is "completed","offline_partial_server_guard");
            });
            Check("read_partial_getter_error_retained",()=>{
                var trace=NewReadTrace(new());try{trace.Step<string>(ReadStepId.ValueStringGet,()=>throw new InvalidOperationException("do_not_export_external_message"),v=>DiagnosticText(v));}catch(InvalidOperationException e){trace.Failed(e);}
                var safe=JsonSerializer.Serialize(trace.Attempt,Json);Need(trace.Attempt["failed_step"] is "ValueStringGet"&&((ErrorInfo)trace.Attempt["error"]!).AssertionCode is null&&!safe.Contains("do_not_export_external_message",StringComparison.Ordinal),"offline_partial_getter_error");
            });
            Check("read_text_bounded_unknown",()=>{
                var shape=DiagnosticText("unknown_private_word ☃");Need(shape["diagnostic_text"] is null&&shape["hash_computed"] is true&&!JsonSerializer.Serialize(shape,Json).Contains("unknown_private_word",StringComparison.Ordinal),"offline_unknown_text_shape");
            });
            Check("read_text_overlong",()=>{
                var shape=DiagnosticText(new string('x',121));Need(shape["hash_computed"] is false&&shape["utf8_sha256"] is null&&shape["category_counts"] is null&&shape["diagnostic_text"] is null,"offline_overlong_text_bound");
            });
            Check("read_server_null_attempt_retained",()=>{
                var trace=NewReadTrace(new());var f=new FakeServerProjection(false);try{ObserveServerProjection(true,1,true,f.IsNull,f.IsNullAsync,f.Text,trace).GetAwaiter().GetResult();}catch(InvalidOperationException e){trace.Failed(e);}
                Need(f.AsyncCalls==1&&f.TextCalls==0&&trace.Attempt["failed_step"] is "ServerNullGuard"&&((ServerNullReceipt)trace.Attempt["server_null_receipt"]!).IsNull==false,"offline_null_projection_partial");
            });
            Check("read_partial_not_complete",()=>{
                var sink=new Dictionary<string,object?>{["observations"]=new List<object>()};var trace=NewReadTrace(sink);try{trace.StepAsync(ReadStepId.GenericGetter,()=>Task.FromException<object?>(new InvalidCastException()),v=>Observe(()=>v)).GetAwaiter().GetResult();}catch(InvalidCastException e){trace.Failed(e);}
                Need(trace.Attempt["completed"] is false&&((List<object>)sink["observations"]!).Count==0&&!sink.ContainsKey("candidate_acceptance"),"offline_partial_not_acceptance");
            });
            Check("interval_text_observed_channels_control",()=>{
                const string value="INTERVAL '000000000 00:00:00.000000' DAY(9) TO SECOND(6)",server="INTERVAL '0 0:0:0.000000' DAY(9) TO SECOND(6)";var expected=new IntervalTextFields(0,0,0,0,0,false);
                Need(GuardReadText(value,"I",false,IntervalMetadata())==value&&GuardReadText(server,"I",true,IntervalMetadata())==server&&ParseIntervalText(value,1686,false)==expected&&ParseIntervalText(server,1686,true)==expected&&Hash(System.Text.Encoding.UTF8.GetBytes(value))=="de042e037a898ddb3fd3fa1b29b5f45152ffd3609fe3dfba767578434ce57ca1"&&Hash(System.Text.Encoding.UTF8.GetBytes(server))=="49a6b8304f47f44ea1a1a7755100e85570bfe8c0ed1858b4b806f75de8e7e5b7","offline_observed_interval_channels");Reject(()=>ParseIntervalText(server,1686,false));
            });
            Check("interval_text_declared_positive_controls",()=>{
                foreach(int packed in new[]{1574,1686})foreach(var fields in new[]{new IntervalTextFields(0,0,0,0,0,false),new IntervalTextFields(1,2,3,4,5,false),new IntervalTextFields(25,23,59,59,123456,true)})
                {string canonical=CanonicalIntervalText(fields,packed);Need(ParseIntervalText(canonical,packed,false)==fields&&ParseIntervalText(canonical,packed,true)==fields,"offline_declared_interval_canonical");}
                Need(ParseIntervalText("INTERVAL '+1 2:03:4.000005' DAY(2) TO SECOND(6)",1574,true)==new IntervalTextFields(1,2,3,4,5,false),"offline_server_widths_sign");
                Reject(()=>GuardReadText("INTERVAL '01 02:03:04.000005' DAY(2) TO SECOND(6)","J",true,IntervalMetadata()));
            });
            Check("interval_text_value_width_refused",()=>{
                foreach(string text in new[]{"INTERVAL '0 00:00:00.000000' DAY(9) TO SECOND(6)","INTERVAL '000000000 0:00:00.000000' DAY(9) TO SECOND(6)","INTERVAL '000000000 00:0:00.000000' DAY(9) TO SECOND(6)","INTERVAL '000000000 00:00:0.000000' DAY(9) TO SECOND(6)"})Reject(()=>ParseIntervalText(text,1686,false));
            });
            Check("interval_text_value_sign_refused",()=>{
                Reject(()=>ParseIntervalText("INTERVAL '+000000001 00:00:00.000000' DAY(9) TO SECOND(6)",1686,false));Reject(()=>ParseIntervalText("INTERVAL '-000000000 00:00:00.000000' DAY(9) TO SECOND(6)",1686,false));Reject(()=>ParseIntervalText("INTERVAL '-0 0:0:0.000000' DAY(9) TO SECOND(6)",1686,true));
            });
            Check("interval_text_fraction_refused",()=>{
                foreach(string fraction in new[]{"",".00000",".0000000"}){string text="INTERVAL '000000000 00:00:00"+fraction+"' DAY(9) TO SECOND(6)";Reject(()=>ParseIntervalText(text,1686,false));Reject(()=>ParseIntervalText(text,1686,true));}
            });
            Check("interval_text_server_domain_refused",()=>{
                foreach(string body in new[]{"0 000:0:0.000000","0 24:0:0.000000","0 0:60:0.000000","0 0:0:60.000000","100 0:0:0.000000"})Reject(()=>ParseIntervalText("INTERVAL '"+body+"' DAY(2) TO SECOND(6)",1574,true));
            });
            Check("interval_text_wrapper_qualifier_refused",()=>{
                foreach(string text in new[]{"0 0:0:0.000000","INTERVAL '0 0:0:0.000000'","'0 0:0:0.000000' DAY(9) TO SECOND(6)","INTERVAL '0 0:0:0.000000' DAY(2) TO SECOND(6)","INTERVAL '0 0:0:0.000000' DAY(9) TO SECOND(5)","INTERVAL '0 0:0:0.000000' YEAR(9) TO SECOND(6)","INTERVAL -'0 0:0:0.000000' DAY(9) TO SECOND(6)","INTERVAL '--0 0:0:0.000000' DAY(9) TO SECOND(6)","INTERVAL '０ 0:0:0.000000' DAY(9) TO SECOND(6)"," INTERVAL '0 0:0:0.000000' DAY(9) TO SECOND(6)","INTERVAL '0 0:0:0.000000' DAY(9) TO SECOND(6)\n"})Reject(()=>ParseIntervalText(text,1686,true));
            });
            Check("interval_text_numeric_mutation_detected",()=>{
                var expected=new IntervalTextFields(1,2,3,4,5,false);Need(ParseIntervalText("INTERVAL '1 2:3:4.000005' DAY(9) TO SECOND(6)",1686,true)==expected,"offline_interval_mutation_control");
                foreach(string body in new[]{"-1 2:3:4.000005","2 2:3:4.000005","1 3:3:4.000005","1 2:4:4.000005","1 2:3:5.000005","1 2:3:4.000006"})Need(ParseIntervalText("INTERVAL '"+body+"' DAY(9) TO SECOND(6)",1686,true)!=expected,"offline_interval_mutation_not_equal");Reject(()=>ParseIntervalText("INTERVAL '1 2:3:4.000005' DAY(9) TO SECOND(6)",1574,true));
            });
            Check("interval_outer_minus_observed_control",()=>{
                const string value="INTERVAL '-000000001 01:00:00.123456' DAY(9) TO SECOND(6)",server="INTERVAL -'1 1:0:0.123456' DAY(9) TO SECOND(6)";var expected=new IntervalTextFields(1,1,0,0,123456,true);byte[] raw=Convert.FromHexString("FFFFFFFFFFFFFFFF0000000000000000C01DFEFF96060000");
                int I(int index)=>System.Buffers.Binary.BinaryPrimitives.ReadInt32LittleEndian(raw.AsSpan(index*4,4));long ticks=I(0)*864000000000L+I(1)*36000000000L+I(2)*600000000L+I(3)*10000000L+I(4)*10L;
                Need(I(5)==1686&&ticks==-900001234560&&Hash(raw)=="1f4635fd456113d5f04b7d24fc3efd3b6dd3ae510b986160c61c95625b971540"&&Hash(System.Text.Encoding.UTF8.GetBytes(value))=="57fafeae47ce4d94248ff102710b9980973288058bc4cf17083a609d5572704e"&&Hash(System.Text.Encoding.UTF8.GetBytes(server))=="c35bb5bc06cc10a5245f3f051d3688a2177b06c54c48f6d5c98d21cc444fac36"&&ParseIntervalText(value,1686,false)==expected&&ParseIntervalText(server,1686,true)==expected&&GuardReadText(server,"I",true,IntervalMetadata())==server,"offline_observed_outerminus");Reject(()=>ParseIntervalText(server,1686,false));
            });
            Check("interval_outer_minus_positive_controls",()=>{
                foreach(int packed in new[]{1574,1686}){int leading=packed==1574?2:9;var expected=new IntervalTextFields(1,2,3,4,5,true);Need(ParseIntervalText("INTERVAL -'1 2:3:4.000005' DAY("+leading+") TO SECOND(6)",packed,true)==expected&&ParseIntervalText("INTERVAL '-1 2:3:4.000005' DAY("+leading+") TO SECOND(6)",packed,true)==expected&&ParseIntervalText(CanonicalIntervalText(expected,packed),packed,false)==expected,"offline_outer_inner_positive");}
            });
            Check("interval_outer_minus_conflict_refused",()=>{
                foreach(string text in new[]{"INTERVAL -'-1 1:0:0.123456' DAY(9) TO SECOND(6)","INTERVAL -'+1 1:0:0.123456' DAY(9) TO SECOND(6)","INTERVAL --'1 1:0:0.123456' DAY(9) TO SECOND(6)","INTERVAL +'1 1:0:0.123456' DAY(9) TO SECOND(6)","INTERVAL - '1 1:0:0.123456' DAY(9) TO SECOND(6)","-INTERVAL '1 1:0:0.123456' DAY(9) TO SECOND(6)","INTERVAL -'1 -1:0:0.123456' DAY(9) TO SECOND(6)","INTERVAL -'0 0:0:0.000000' DAY(9) TO SECOND(6)"})Reject(()=>ParseIntervalText(text,1686,true));
            });
            Check("interval_outer_minus_numeric_mutation_detected",()=>{
                var expected=new IntervalTextFields(1,1,0,0,123456,true);foreach(string text in new[]{"INTERVAL '1 1:0:0.123456' DAY(9) TO SECOND(6)","INTERVAL '+1 1:0:0.123456' DAY(9) TO SECOND(6)","INTERVAL -'2 1:0:0.123456' DAY(9) TO SECOND(6)","INTERVAL -'1 1:0:0.123457' DAY(9) TO SECOND(6)"})Need(ParseIntervalText(text,1686,true)!=expected,"offline_outerminus_mutation");
            });
            Report["status"]="offline_contracts_passed";exit=0;
        }
        catch(Exception e){Report["status"]="offline_contracts_failed";Report["failure"]=Safe(e);}
        Report["schema_version"]=1;Report["task"]="TP-06-supplement";Report["expected_only"]=true;Report["database_accessed"]=false;Report["checks"]=checks;Report["check_count"]=checks.Count;Report["process_exit_code"]=exit;
        try{Write(Output,Report);}catch{return 2;}return exit;
    }
    sealed class FakeRecovery(System.Data.ConnectionState initial):IRecoveryAdapter
    {
        System.Data.ConnectionState current=initial;readonly List<SentFrame> sent=[];public string OldId=>"old";public System.Data.ConnectionState State=>current;public int SentCount=>sent.Count;public SentFrame[] SentSince(int index)=>sent.Skip(index).ToArray();
        internal bool SendOld,WrongOldError,ReuseFreshId,VerifyFailure;internal int FreshScalar=1,OldProbes,CommandDisposes,ConnectionDisposes,FreshOpens,FreshVerifications,FreshDisposes;
        public Task<object?> ProbeOld(){OldProbes++;if(SendOld)sent.Add(new("offline",5,1,1,1,1));if(current is System.Data.ConnectionState.Broken or System.Data.ConnectionState.Closed){if(WrongOldError)throw new NotSupportedException();throw new InvalidOperationException();}return Task.FromResult<object?>(1);}
        public Task DisposeCommand(){CommandDisposes++;return Task.CompletedTask;}public Task DisposeConnection(){ConnectionDisposes++;current=System.Data.ConnectionState.Closed;return Task.CompletedTask;}
        public Task<RecoveryIdentity> OpenFresh(){FreshOpens++;return Task.FromResult(new RecoveryIdentity(ReuseFreshId?"old":"fresh",User,User,"Open"));}public Task<object?> ProbeFresh()=>Task.FromResult<object?>(FreshScalar);
        public Task VerifyFresh(){FreshVerifications++;if(VerifyFailure)throw new InvalidOperationException();return Task.CompletedTask;}public Task DisposeFresh(){FreshDisposes++;return Task.CompletedTask;}
    }
    sealed class FakeServerProjection(bool isNull)
    {
        internal int SyncCalls,AsyncCalls,TextCalls;
        internal bool IsNull(){SyncCalls++;return isNull;}
        internal Task<bool> IsNullAsync(){AsyncCalls++;return Task.FromResult(isNull);}
        internal string Text(){TextCalls++;return "2026-07-23";}
    }
    sealed class FakeOwner:IOwnershipDatabase
    {
        internal bool Present,FreshPresent;internal int Creates,Drops,FreshChecks;internal string? Default;
        public void VerifyIdentity(){}
        public bool Exists(Ownership o)=>Present;
        public void Create(Ownership o){Creates++;Present=true;}
        public string? ReadMarkerDefault(Ownership o)=>Default;
        public void Drop(Ownership o){Drops++;Present=false;}
        public bool FreshAbsent(Ownership o){FreshChecks++;return !FreshPresent&&!Present;}
    }
}
