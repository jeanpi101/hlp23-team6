module Simulator

open DiagramTypes
open JSHelpers

type private Bit = Zero | One

// The next types are not strictly necessary,
// but help in understanding what is what.
type private ComponentId      = | ComponentId of string
type private InputPortId      = | InputPortId of string
type private OutputPortId     = | OutputPortId of string
type private InputPortNumber  = | InputPortNumber of int
type private OutputPortNumber = | OutputPortNumber of int

type private SimulationComponent = {
    Id : ComponentId
    // Mapping from each input port number to its value (it will be set
    // during the simulation process).
    // TODO: maybe using a list would improve performace?
    Inputs : Map<InputPortNumber, Bit>
    // Mapping from each output port number to all of the ports and
    // Components connected to that port.
    Outputs : Map<OutputPortNumber, (ComponentId * InputPortNumber) list>
    // Function that takes the inputs and transforms them into the outputs.
    // The size of input map, must be as expected by otherwhise the reducer will
    // return None (i.e. keep on waiting for more inputs to arrive).
    // The reducer should fail if more inputs than expected are received.
    Reducer : Map<InputPortNumber, Bit> -> Map<OutputPortNumber, Bit> option
}

// Map every ComponentId to its SimulationComponent.
type private SimulationGraph = Map<ComponentId, SimulationComponent>

// Simulating a circuit has three phases:
// - building a simulation graph made of SimulationComponents.
// - analyse the graph to look for errors, such as unconnected ports,
//   combinatorial loops, etc... TODO
// - setting the values of the input nodes of the graph to kickstart the
//   simulation process.

// Ports constraints:
// - Source ports must be output ports.
// - Target ports must be input ports.
// - All ports have at least one connection that touches them.
// - Input ports have precisely one connection that touches them.

// In order to create the graph that represents the circuit ready to be
// simulated, iterate over each node and try to "connect" its outputs.

// During simulation, a Component can call the Reducer function only when
// all of the inputs have a value. Once this happens, it will calculate its
// outputs and set them in the next simulationComponent(s).

// Ports in component should always have a portNumber, ports in connections
// should have None.
let getPortNumberOrFail port =
    match port with
    | None -> failwithf "what? Component ports should always have a portNumber"
    | Some p -> p

let private bitAnd bit0 bit1 =
    match bit0, bit1 with
    | One, One -> One
    | _, _ -> Zero

// TODO: remove.
let private testState : CanvasState = ([
    {
        Id = "input0";
        Type = Input;
        Label = "input0";
        InputPorts = [];
        OutputPorts = [
            {
                Id = "input0-OP0";
                PortNumber = Some 0
                PortType = PortType.Output;
                HostId = "input0"
            }
        ];
        X = 326;
        Y = 440
    }
    {
        Id = "input1";
        Type = Input;
        Label = "input1";
        InputPorts = [];
        OutputPorts = [
            {
                Id = "input1-OP0";
                PortNumber = Some 0
                PortType = PortType.Output;
                HostId = "input1"
            }
        ];
        X = 321;
        Y = 492
    }
    {
        Id = "And";
        Type = And;
        Label = "And";
        InputPorts = [
            {
                Id = "And-IP0";
                PortNumber = Some 0
                PortType = PortType.Input;
                HostId = "And"};
            {
                Id = "And-IP1";
                PortNumber = Some 1
                PortType = PortType.Input;
                HostId = "And"
            };
        ]
        OutputPorts = [
            {
                Id = "And-OP0";
                PortNumber = Some 0
                PortType = PortType.Output;
                HostId = "And"
            }
        ];
        X = 428;
        Y = 459
    }
    {
        Id = "output";
        Type = Output;
        Label = "output";
        InputPorts = [
            {
                Id = "output-IP0";
                PortNumber = Some 0
                PortType = PortType.Input;
                HostId = "output"
            }
        ];
        OutputPorts = [];
        X = 610;
        Y = 469
    }
    ],
    [
    {
        Id = "TopLeft";
        Source = {
            Id = "input0-OP0";
            PortNumber = None
            PortType = PortType.Output;
            HostId = "input0"
        };
        Target = {
            Id = "And-IP0";
            PortNumber = None
            PortType = PortType.Input;
            HostId = "And"
        };
    }
    {
        Id = "BottomLeft";
        Source = {
            Id = "input1-OP0";
            PortNumber = None
            PortType = PortType.Output;
            HostId = "input1"
        }
        Target = {
            Id = "And-IP1";
            PortNumber = None
            PortType = PortType.Input;
            HostId = "And"
        }
    }
    {
        Id = "Right";
        Source = {
            Id = "And-OP0";
            PortNumber = None
            PortType = PortType.Output;
            HostId = "And"
        }
        Target = {
            Id = "output-IP0";
            PortNumber = None
            PortType = PortType.Input;
            HostId = "output"
        }
    }
    ])

// For each source port in the connections, keep track of the ports it targets.
// TODO: check all source ports are Outputs, all target ports are Input.
let private buildSourceToTargetPort
        (connections : Connection list)
        : Map<OutputPortId, (ComponentId * InputPortId) list> =
    (Map.empty, connections) ||> List.fold (fun map conn ->
        let key = OutputPortId conn.Source.Id
        let target = ComponentId conn.Target.HostId, InputPortId conn.Target.Id
        // Unfortunately, it makes no sense to extract the PortNumber here as
        // it is always set to None for ports in connections.
        // Append the new target to the list associated with the key.
        let newValue =
            match map.TryFind key with
            | None -> [target]
            | Some oldValue -> target :: oldValue
        map.Add (key, newValue)
    )

// For each input port in each component, map it to its port number.
let private mapInputPortIdToPortNumber
        (components : Component list)
        : Map<InputPortId, InputPortNumber> =
    (Map.empty, components) ||> List.fold (fun map comp ->
        (map, comp.InputPorts) ||> List.fold (fun map port ->
            map.Add (InputPortId port.Id,
                     InputPortNumber (getPortNumberOrFail port.PortNumber))
        )
    )

let private assertNotTooManyInputs
        (inputs : Map<InputPortNumber, Bit>)
        (cType : ComponentType)
        (expected : int)
        : unit =
    if inputs.Count > expected
    then failwithf "what? assertNotTooManyInputs failed for %A: %d > %d" cType inputs.Count expected  

let rec private getValuesForPorts
        (inputs : Map<InputPortNumber, Bit>)
        (portNumbers : InputPortNumber list)
        : (Bit list) option =
    match portNumbers with
    | [] -> Some []
    | portNumber :: portNumbers' ->
        match inputs.TryFind portNumber with
        | None -> None
        | Some bit ->
            match getValuesForPorts inputs portNumbers' with
            | None -> None
            | Some bits -> Some <| bit :: bits 

let private getReducer
        (componentType : ComponentType)
        : Map<InputPortNumber, Bit> -> Map<OutputPortNumber, Bit> option =
    match componentType with
    | Input -> (fun inputs ->
            assertNotTooManyInputs inputs Input 1
            // Simply forward the input.
            match getValuesForPorts inputs [InputPortNumber 0] with
            | None -> None // Wait for more inputs.
            | Some [bit] -> Some <| Map.empty.Add (OutputPortNumber 0, bit)
            | _ -> failwithf "what? Unexpected inputs to And: %A" inputs
        )
    | Output -> (fun inputs ->
            log "SIMULATION OUTPUT"
            inputs |> sprintf "%A" |> log 
            None // TODO
        )
    | And -> (fun inputs ->
            assertNotTooManyInputs inputs And 2
            match getValuesForPorts inputs [InputPortNumber 1; InputPortNumber 0] with
            | None -> None // Wait for more inputs.
            | Some [bit0; bit1] -> Some <| Map.empty.Add (OutputPortNumber 0, bitAnd bit1 bit0)
            | _ -> failwithf "what? Unexpected inputs to And: %A" inputs
        )
    | _ -> failwithf "what? Reducer for %A not implemented" componentType

let private buildSimulationComponent
        (sourceToTargetPort : Map<OutputPortId, (ComponentId * InputPortId) list>)
        (portIdToPortNumber : Map<InputPortId, InputPortNumber>)
        (comp : Component)
        : SimulationComponent =
    // Remove portIds and use portNumbers instead.
    let mapPortIdsToPortNumbers
            (targets : (ComponentId * InputPortId) list)
            : (ComponentId * InputPortNumber) list =
        targets |> List.map (fun (compId, portId) ->
            match portIdToPortNumber.TryFind <| portId with
            | None -> failwithf "what? Input port with portId %A has no portNumber associated" portId
            | Some portNumber -> compId, portNumber
        )
    let outputs =
        comp.OutputPorts
        |> List.map (fun port ->
            match sourceToTargetPort.TryFind <| OutputPortId port.Id with
            | None -> failwithf "what? Unconnected output port %s in comp %s" port.Id comp.Id
            | Some targets -> OutputPortNumber (getPortNumberOrFail port.PortNumber),
                              mapPortIdsToPortNumbers targets
        )
        |> Map.ofList
    {
        Id = ComponentId comp.Id
        Inputs = Map.empty
        Outputs = outputs
        Reducer = getReducer comp.Type
    }

let private buildSimulationGraph (canvasState : CanvasState) : SimulationGraph =
    let components, connections = canvasState
    let sourceToTargetPort = buildSourceToTargetPort connections
    let portIdToPortNumber = mapInputPortIdToPortNumber components
    let mapper = buildSimulationComponent sourceToTargetPort portIdToPortNumber
    components
    |> List.map (fun comp -> ComponentId comp.Id, mapper comp)
    |> Map.ofList

let rec private simulateStep
        (graph : SimulationGraph)
        (compId : ComponentId)
        (input : InputPortNumber * Bit)
        : SimulationGraph =
    // Extract component.
    let comp = match graph.TryFind compId with
               | None -> failwithf "what? Could not find component %A in simulationStep" compId
               | Some c -> c
    // Add input to the simulation component.
    let comp = { comp with Inputs = comp.Inputs.Add input }
    let graph = graph.Add (comp.Id, comp)
    // Try to reduce the component.
    match comp.Reducer comp.Inputs with
    | None -> graph // Keep on waiting for more inputs.
    | Some outputMap ->
        // Received enough inputs and produced an output.
        // Propagate each output produced to all the ports connected.
        (graph, outputMap) ||> Map.fold (fun graph outPortNumber bit ->
            match comp.Outputs.TryFind outPortNumber with
            | None -> failwithf "what? Reducer produced inexistent output portNumber %A in component %A" outPortNumber comp
            | Some targets ->
                // Trigger simulation step with the newly produced input in
                // every target.
                (graph, targets) ||> List.fold (fun graph (nextCompId, nextPortNumber) ->
                    simulateStep graph nextCompId (nextPortNumber, bit)
                )
        )

let simulate () =
    log "START"
    let graph = buildSimulationGraph testState
    let graph = simulateStep graph (ComponentId "input0") (InputPortNumber 0, One)
    let graph = simulateStep graph (ComponentId "input1") (InputPortNumber 0, Zero)
    ()
