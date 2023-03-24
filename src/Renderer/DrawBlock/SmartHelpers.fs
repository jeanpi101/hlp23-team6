﻿module SmartHelpers
open CommonTypes
open DrawHelpers
open DrawModelType
open DrawModelType.SymbolT
open DrawModelType.BusWireT
open BusWire
open BusWireUpdateHelpers
open SymbolUpdate
open PopupDrawingView
open Optics
open Operators
open Fable.React
open Fable.React.Props
open Fulma
open SymbolUpdate
open Symbol
open JSHelpers

//-----------------------------------------------------------------------------------------------//
//---------------------------HELPERS FOR SMART DRAW BLOCK ADDITIONS------------------------------//
//-----------------------------------------------------------------------------------------------//

(*
HOW TO USE THIS MODULE.
(1) Add well-documented useful functions - see updateModelSymbols and updateModelWires
    for examples. You do not need to add performance information as in updateModelSymbols. 
    Your priority should be writing clear code. Try to avoid very inefficient implementations
    if possible (e.g. 100X slower than a similar complexity solution), but do not worry 
    about this.
(2) Note from my examples distinction between XML documentation and additional details
    in header comments.
(3) HLP23: Note comments here labelled "HLP23" which are for HLP23 class and would be deleted in
    production (Group phase) code.
(2) HLP23: Each function must have a single author specified by "HLP23: AUTHOR" in an XML comment
    as in my example: give name as Family name only (unique within teams).
(3) HLP23: Inform other members that you have written a function they could maybe use.
(4) HLP23: If two people end up with near-identical functions here team phase can rationalise if
    needed normally you are expected to share when this makes code writing faster.
(5) Note best practice here using Optics for nested record update. This is NOT curently required
    in Issie but used appropriately results in better code. Use it if you are comfortable doing so.
(5) Note on qualifying types: do this when not doing it would be ambiguous - e.g. here
    the BusWire and Symbol Model types.
(6) Note on code layout. A limit of 100 characters per line is used here. Seems about right.
*)

//----------------------------------------------------------------------------------------------//

/// Update BusWire model with given symbols. Can also be used to add new symbols.
/// This uses a fold on the Map to add symbols which makes it fast in the case that the number
/// of symbols added is very small.
//  Performance scales as O(M*log2(N)) - M = number of symbols added, N = number of existing
//  Symbols. Changing large maps is relatively expensive hence the care here.
//  This function uses best practice for nested record update with Optics. See Wiki for
//  details. Note that Optics are probably a little bit slower than F# record update (factor of 2)
//  however in this case it does not matter because the time taken is << the Map update time.
/// HLP23: AUTHOR Clarke
let updateModelSymbols 
    (model: BusWireT.Model) 
    (symbols: Symbol list)
        : BusWireT.Model =
    // HLP23: note on fold implementation. symMap is both argument and result of the
    // fold function => sequential set of updates. In thsi case much more efficient than Map.map
    // over all symbols.
    // HLP23 - see also similar updateModelWires
    let symbols' =
        (model.Symbol.Symbols,symbols)
        ||> List.fold (fun symMap symToAdd -> Map.add symToAdd.Id symToAdd symMap)
    Optic.set (symbol_ >-> symbols_) symbols' model



//Helper function to GetSelectComponenet takes in a component type and returns a bool. 
//It checks if the component is a component that contains a select port
//HLP23: AUTHOR Khoury
let CheckSelectComponent 
    (inputType : ComponentType)
        : bool =
    match inputType with
        | Mux2 -> true
        | Mux4 -> true
        | Mux8 -> true
        | Demux2 -> true
        | Demux4 -> true
        | Demux8 -> true
        | _ -> false

//Helper function to CheckforFlip takes a DrawModelType.SymbolT.Model, a BusWireT.Model, a list of wires, and a symbol and returns a triple of 
//a symbol, a symbol, and a BusWireT.Model. 
// It gets the length of teh select wire if the compenent is flipped and if it is not flipped. It returns the two length and the flipped component.
//HLP23: AUTHOR Khoury
let GetSelectWireLength
    (sModel : DrawModelType.SymbolT.Model)
    (wModel: BusWireT.Model)
    (wireList: Wire list)
    (inputSymbol : Symbol)
        : float*float*Symbol*BusWireT.Model=

        let symOrderMap = inputSymbol.PortMaps.Order 
        let findOrderList map edge = map |> Map.find edge


        let oldWires = List.map (autoroute {wModel with Symbol = sModel}) wireList

        let SelectPort = match List.isEmpty (findOrderList symOrderMap Edge.Bottom) with
                                | true -> (findOrderList symOrderMap Edge.Top)[0]
                                | false -> (findOrderList symOrderMap Edge.Bottom)[0]

        let newSymbol =  flipSymbol FlipVertical inputSymbol 
        let oldSelectWire = oldWires |> List.filter (fun wire -> $"{wire.InputPort}" = SelectPort || $"{wire.OutputPort}" = SelectPort)

        if List.isEmpty oldSelectWire then 
            0.0, 0.0, inputSymbol, wModel
        else
            let newModel = {wModel with Symbol = {sModel with Symbols = Map.add inputSymbol.Id newSymbol sModel.Symbols}}
            let newSelectWire = autoroute newModel oldSelectWire[0]

            let lengthOldSelect =(oldSelectWire[0].Segments) |> List.fold (fun acc x -> acc + abs x.Length) 0.0 
            let lengthNewSelect = (newSelectWire.Segments)  |> List.fold (fun acc x -> acc+ abs  x.Length) 0.0


            lengthNewSelect, lengthOldSelect, newSymbol, newModel

//Uses helper functions to get the lengths of the select wires in all cases. Checks which one is better and returns the best performing symbols and model.
//HLP23: AUTHOR Khoury
let CheckforFlip  
    (sModel : DrawModelType.SymbolT.Model)
    (wireList: Wire list)
    (wModel: BusWireT.Model)
    (symbolToOrder: Symbol)
    (otherSymbol: Symbol)
        : Symbol*BusWireT.Model*bool =

    let newSmodel = {sModel with Symbols = Map.add symbolToOrder.Id symbolToOrder sModel.Symbols}
    let symComponentType = otherSymbol.Component.Type

    match CheckSelectComponent symComponentType with 
        | true ->
            let lengthNewSelect, lengthOldSelect , newSymbol, newModel= GetSelectWireLength newSmodel wModel wireList otherSymbol
            match lengthNewSelect < lengthOldSelect with
               | true -> newSymbol, newModel, true
               | false -> otherSymbol, wModel, false 
        | false -> otherSymbol, wModel, false

//Helper function to SortPorts takes in connected ports Ids (strings) and the Edges they are on in a quadruple list and a list of ports Ids (string)
//It sorts the quadruple list by the first element of the quadruple which is the port Id of the otherSymbol with the same order as the list of ports Ids
//HLP23: AUTHOR Khoury
let sortByOther 
    (list2 : string list)
    (list1 : (string*string*Edge*Edge) list) 
        : (string*string*Edge*Edge) list=
    printfn "Here2"
    list1
    |> List.sortBy (fun (x,_,_,_) -> List.findIndex (fun s -> s = x) list2)

//Helper function to SortPorts takes in a list of quadruple lists and returns an Edge
//It gets the edge that the ports are connected to on the otherSymbol
//HLP23: AUTHOR Khoury
let OtherSymbolOrientation 
    (list: (string*string*Edge*Edge) list)
        : Edge =
    list 
    |> List.map (fun (_,_,x,_) -> x)
    |> List.head

//Helper function to SortPorts takes in a list of quadruple lists and a symbol returns an Edge.
//It resorts the Ports in a way that the first element is always the one of the symbolToOrder
//HLP23: AUTHOR Khoury
let sortInputOutput 
    (symbolToOrder: Symbol)
    (connectedPorts:(InputPortId*OutputPortId)list)
        : (string*string) list =
        connectedPorts
        |> List.map (fun x  -> 
                match symbolToOrder.PortMaps.Orientation |> Map.containsKey ($"{fst x}") with 
                | true -> ($"{snd x}",$"{fst x}")
                | false -> ($"{fst x}",$"{snd x}"))

// Uses helper functions to sort the ports in the correct order. It returns a list of quadruple lists. 
//Each quadruple list contains the ports that are connected to the same edges on the otherSymbol and the symbolToOrder
//HLP23: AUTHOR Khoury
let sortPorts 
    (connectedPorts:(InputPortId*OutputPortId)list)
    (symbolToOrder: Symbol)
    (otherSymbol: Symbol)
        : ((string*string*Edge*Edge) list)list =
    
    let otherOrderMaps = otherSymbol.PortMaps.Order
    let tryFindOrientation x sym = Map.tryFind x sym.PortMaps.Orientation

    connectedPorts
    //Put the Ports in correct order (Input, Output) depending on which is the symbolToOrder
    |> sortInputOutput symbolToOrder 
    //Group the Ports by the matching edges
    |>List.collect (fun (x, y) ->
            match tryFindOrientation x otherSymbol, tryFindOrientation y symbolToOrder with
                | Some e1, Some e2 -> [(e1, e2, x, y)]
                | _ -> []
            )
    |> List.groupBy (fun (e1, e2, _, _) -> e1, e2)
    |> List.map (fun (_, group) -> List.map (fun (e1, e2, x, y) -> (x,y,e1,e2)) group)
    |> List.map (fun x -> 
                                            sortByOther  (otherOrderMaps 
                                                                      |> Map.find (OtherSymbolOrientation x))x)

/// Update BusWire model with given wires. Can also be used to add new wires.
/// This uses a fold on the Map to add wires which makes it fast in the case that the number
/// of wires added is small.
//  Performance scales as O(M*log2(N)) - M = number of wires added, N = number of existing
//  wires. Changing large maps is relatively expensive hence the care here.
//  This function uses best practice for nested record update with Optics. See Wiki for
//  details. Note that Optics are probably a little bit slower than F# record update (factor of 2)
//  however in this case it does not matter because the time taken is << the Map update time.
/// HLP23: AUTHOR Clarke
let updateModelWires 
    (model: BusWireT.Model) 
    (wiresToAdd: Wire list)
        : BusWireT.Model =
    //
    // HLP23: note on fold implementation. In this (typical) example Map is
    // sequentially updated by the fold. A common and difficult to see coding mistake is to use the 
    // original wireMap (argument of Optic map function) not the updated one (wireMap argument of 
    // List.map folder) in the fold function! That is not possible here because both have the same 
    // name so the inner bound updated wireMap is always what is used in the folder function. 
    // This is good practice, and if you have ever debugged this type of mistake you will know it
    // is very necessary!

    // HLP23: note on this use of Optics.map in a pipeline. It is more "functional" than the 
    // equivalent implementation using a let definition and Optics.set. Is it clearer? Or less clear? 
    // Standard logic says we should prefer the pipeline if the name of the let definition adds 
    // nothing which is the case here. I have given you both ways of using Optics here so you can 
    // compare the two implementations and decide. NB - you are NOT required to use Optics in your 
    // own code.
    //
    // HLP23: Note how multiple updates to different parts of the model can be neatly pipelined 
    // like this using a separate Optic.map or Optic.set for each.
    //
    // HLP23: note that if the operation here was larger or part of some pipeline the
    // 2nd argument to Optic.map - which defines the model change - could be given a name and 
    // turned into a local function making the Optic.map line like:
    // |> Optic.map wires_ myNameForThisWireMapUpdate
    model
    |> Optic.map wires_ (fun wireMap  ->
        (wireMap,wiresToAdd)
        ||> List.fold (fun wireMap wireToAdd -> Map.add wireToAdd.WId wireToAdd wireMap))


///HLP 23: AUTHOR Klapper - Returns the middle segment of a given wire
let getMiddleSegment (model : Model) (wireId:ConnectionId) =
    let wire = model.Wires[wireId]
    match wire.Segments.Length with
    | 6 -> getSegmentFromId model (2, wireId)
    | 7 -> getSegmentFromId model (3, wireId)
    | 9 -> getSegmentFromId model (5, wireId)
    | 8 -> getSegmentFromId model (3, wireId)
    | x -> getSegmentFromId model (x/2, wireId)



///HLP 23: AUTHOR Klapper - Checks whether the wire terminates in one of the components
let isWireTerminatingInComponents (model : BusWireT.Model) (comIdLst : List<ComponentId>) (wireId : ConnectionId) =
    let wire = model.Wires[wireId]
    let outputPort =  model.Symbol.Ports[wire.OutputPort |> function | OutputPortId s -> s]
    comIdLst|> List.contains (ComponentId outputPort.HostId)



///HLP 23: AUTHOR Klapper - Checks whether the given position is inside the bounding box
let isPositionInBounds (bound : BoundingBox) (pos : XYPos) =
    match (pos.X >= bound.TopLeft.X, pos.X <= bound.TopLeft.X + bound.W, pos.Y >= bound.TopLeft.Y , pos.Y <= bound.TopLeft.Y + bound.H) with
    | true,true,true,true -> true
    | _ -> false


///HLP 23: AUTHOR Klapper - Checks whether a wire is connected on the left or right side of a bounding box
let isWireConnectedOnLeft (model : Model) (bounds : BoundingBox) (wireId) (orientation: Orientation)=
    let wire = model.Wires[wireId]
    if orientation = Vertical then
        if isPositionInBounds {bounds with W = bounds.W / 2.0} wire.StartPos || isPositionInBounds {bounds with W = bounds.W / 2.0}  wire.EndPos then true else false
    else 
        if isPositionInBounds {bounds with H = bounds.H / 2.0} wire.StartPos || isPositionInBounds {bounds with H = bounds.H / 2.0}  wire.EndPos then true else false


///HLP 23: AUTHOR Klapper - Returns the component on the end of the wire
let getEndComponent (model : DrawModelType.BusWireT.Model) (wire : Wire) =
    let endCompId = model.Symbol.Ports[Symbol.getInputPortIdStr wire.InputPort].HostId
    model.Symbol.Symbols[ComponentId endCompId].Component



///HLP 23: AUTHOR Klapper - Returns the component on the start of the wire
let getStartComponent (model : DrawModelType.BusWireT.Model) (wire : Wire) =
    let startCompId = model.Symbol.Ports[Symbol.getOutputPortIdStr wire.OutputPort].HostId
    model.Symbol.Symbols[ComponentId startCompId].Component


///HLP 23: AUTHOR Klapper - Checks whether the wire is connected to a wire label
let isWireConnectedToLabel (model : Model) (wire : Wire) =
    let startComp = getStartComponent model wire
    let endComp = getEndComponent model wire
    match startComp.Type, endComp.Type with 
    | ComponentType.IOLabel, _ -> true
    | _, ComponentType.IOLabel -> true
    | _ -> false


///HLP 23: AUTHOR Klapper - Adds to rotations together, legacy function could be useful for later applications.
let addDegree deg1 deg2 =
    match deg1,deg2 with
    | Degree0, deg -> deg
    | deg, Degree0 -> deg
    | Degree90, Degree90 -> Degree180
    | Degree90, Degree180 -> Degree270
    | Degree90, Degree270 -> Degree0
    | Degree180, Degree90 -> Degree270
    | Degree180, Degree180 -> Degree0
    | Degree180, Degree270 -> Degree90
    | Degree270, Degree90 -> Degree0
    | Degree270, Degree180 -> Degree90
    | Degree270, Degree270 -> Degree180


   
/// <summary>HLP 23: AUTHOR Ismagilov - Get the bounding box of multiple selected symbols</summary>
/// <param name="symbols"> Selected symbols list</param>
/// <returns>Bounding Box</returns>
let getBlock 
        (symbols:Symbol List) :BoundingBox = 

    let maxXsym = (List.maxBy (fun (x:Symbol) -> x.Pos.X+(snd (getRotatedHAndW x))) symbols)
    let maxX = maxXsym.Pos.X + (snd (getRotatedHAndW maxXsym))

    let minX = (List.minBy (fun (x:Symbol) -> x.Pos.X) symbols).Pos.X

    let maxYsym = List.maxBy (fun (x:Symbol) -> x.Pos.Y+(fst (getRotatedHAndW x))) symbols
    let maxY = maxYsym.Pos.Y + (fst (getRotatedHAndW maxYsym))

    let minY = (List.minBy (fun (x:Symbol) -> x.Pos.Y) symbols).Pos.Y

    {TopLeft = {X = minX; Y = minY}; W = maxX-minX; H = maxY-minY}


/// <summary>HLP 23: AUTHOR Ismagilov - Takes a point Pos, a centre Pos, and a rotation type and returns the point flipped about the centre</summary>
/// <param name="point"> Original XYPos</param>
/// <param name="center"> The center XYPos that the point is rotated about</param>
/// <param name="rotation"> Clockwise or Anticlockwise </param>
/// <returns>New flipped point</returns>
let rotatePointAboutBlockCentre 
            (point:XYPos) 
            (centre:XYPos) 
            (rotation:RotationType) = 
    let relativeToCentre = (fun x->x - centre)
    let rotateAboutCentre (pointIn:XYPos) = 
        match rotation with 
        | RotateClockwise ->
            
            {X = -pointIn.Y ; Y = pointIn.X}
          
        | RotateAntiClockwise ->
            
            {X = pointIn.Y ; Y = -pointIn.X}
           
    let relativeToTopLeft = (fun x->x + centre)

    point
    |> relativeToCentre
    |> rotateAboutCentre
    |> relativeToTopLeft

/// <summary>HLP 23: AUTHOR Ismagilov - Takes a point Pos, a centre Pos, and a flip type and returns the point flipped about the centre</summary>
/// <param name="point"> Original XYPos</param>
/// <param name="center"> The center XYPos that the point is flipped about</param>
/// <param name="flip"> Horizontal or Vertical flip</param>
/// <returns>New flipped point</returns>
let flipPointAboutBlockCentre 
    (point:XYPos)
    (center:XYPos)
    (flip:FlipType) = 
    match flip with
    | FlipHorizontal-> 
        {X = center.X - (point.X - center.X); Y = point.Y} 
    | FlipVertical -> 
        {X = point.X; Y = center.Y - (point.Y - center.Y)}

/// <summary>HLP 23: AUTHOR Ismagilov - Get the new top left of a symbol after it has been rotated</summary>
/// <param name="rotation"> Rotated CW or AntiCW</param>
/// <param name="h"> Original height of symbol (Before rotation)</param>
/// <param name="w"> Original width of symbol (Before rotation)</param>
/// <param name="sym"> Symbol</param>
/// <returns>New top left point of the symbol</returns>
let adjustPosForBlockRotation
        (rotation:RotationType) 
        (h: float)
        (w:float)
        (pos: XYPos)
         : XYPos =
    let posOffset =
        match rotation with
        | RotateClockwise -> {X=(float)h ;Y=0}
        | RotateAntiClockwise -> { X = 0 ;Y = (float)w }
    pos - posOffset

/// <summary>HLP 23: AUTHOR Ismagilov - Get the new top left of a symbol after it has been flipped</summary>
/// <param name="flip">  Flipped horizontally or vertically</param>
/// <param name="h"> Original height of symbol (Before flip)</param>
/// <param name="w"> Original width of symbol (Before flip)</param>
/// <param name="sym"> Symbol</param>
/// <returns>New top left point of the symbol</returns>
let adjustPosForBlockFlip
        (flip:FlipType) 
        (h: float)
        (w:float)
        (pos: XYPos) =
    let posOffset =
        match flip with
        | FlipHorizontal -> {X=(float)w ;Y=0}
        | FlipVertical -> { X = 0 ;Y = (float)h }
    pos - posOffset

/// <summary>HLP 23: AUTHOR Ismagilov - Rotate a symbol in its block.</summary>
/// <param name="rotation">  Clockwise or Anticlockwise rotation</param>
/// <param name="block"> Bounding box of selected components</param>
/// <param name="sym"> Symbol to be rotated</param>
/// <returns>New symbol after rotated about block centre.</returns>
let rotateSymbolInBlock 
        (rotation: RotationType) 
        (blockCentre: XYPos)
        (sym: Symbol)  : Symbol =
      
    let h,w = getRotatedHAndW sym

    let newTopLeft = 
        rotatePointAboutBlockCentre sym.Pos blockCentre rotation
        |> adjustPosForBlockRotation rotation h w

    let newComponent = { sym.Component with X = newTopLeft.X; Y = newTopLeft.Y}
    
    let newSTransform = 
        match sym.STransform.flipped with
        | true -> 
            {sym.STransform with Rotation = rotateAngle (invertRotation rotation) sym.STransform.Rotation}  
        | _-> 
            {sym.STransform with Rotation = rotateAngle rotation sym.STransform.Rotation}

    { sym with 
        Pos = newTopLeft;
        PortMaps = rotatePortInfo rotation sym.PortMaps
        STransform =newSTransform 
        LabelHasDefaultPos = true
        Component = newComponent
    } |> calcLabelBoundingBox 

/// <summary>HLP 23: AUTHOR Ismagilov - Flip a symbol horizontally or vertically in its block.</summary>
/// <param name="flip">  Flip horizontally or vertically</param>
/// <param name="block"> Bounding box of selected components</param>
/// <param name="sym"> Symbol to be flipped</param>
/// <returns>New symbol after flipped about block centre.</returns>
let flipSymbolInBlock
    (flip: FlipType)
    (blockCentre: XYPos)
    (sym: Symbol) : Symbol =

    let h,w = getRotatedHAndW sym
    //Needed as new symbols and their components need their Pos updated (not done in regular flip symbol)
    let newTopLeft = 
        flipPointAboutBlockCentre sym.Pos blockCentre flip
        |> adjustPosForBlockFlip flip h w

    let portOrientation = 
        sym.PortMaps.Orientation |> Map.map (fun id side -> flipSideHorizontal side)

    let flipPortList currPortOrder side =
        currPortOrder |> Map.add (flipSideHorizontal side ) sym.PortMaps.Order[side]

    let portOrder = 
        (Map.empty, [Edge.Top; Edge.Left; Edge.Bottom; Edge.Right]) ||> List.fold flipPortList
        |> Map.map (fun edge order -> List.rev order)       

    let newSTransform = 
        {flipped= not sym.STransform.flipped;
        Rotation= sym.STransform.Rotation} 

    let newcomponent = {sym.Component with X = newTopLeft.X; Y = newTopLeft.Y}

    { sym with
        Component = newcomponent
        PortMaps = {Order=portOrder;Orientation=portOrientation}
        STransform = newSTransform
        LabelHasDefaultPos = true
        Pos = newTopLeft
    }
    |> calcLabelBoundingBox
    |> (fun sym -> 
        match flip with
        | FlipHorizontal -> sym
        | FlipVertical -> 
            let newblock = getBlock [sym]
            let newblockCenter = newblock.Centre()
            sym
            |> rotateSymbolInBlock RotateAntiClockwise newblockCenter 
            |> rotateSymbolInBlock RotateAntiClockwise newblockCenter)

/// <summary>HLP 23: AUTHOR Ismagilov - Scales selected symbol up or down.</summary>
/// <param name="scaleType"> Scale up or down. Scaling distance is constant</param>
/// <param name="block"> Bounding box of selected components</param>
/// <param name="sym"> Symbol to be rotated</param>
/// <returns>New symbol after scaled about block centre.</returns>
let scaleSymbolInBlock
    //(Mag: float)
    (scaleType: ScaleType)
    (block: BoundingBox)
    (sym: Symbol) : Symbol =

    let symCenter = getRotatedSymbolCentre sym

    //Get x and y proportion of symbol to block
    let xProp, yProp = (symCenter.X - block.TopLeft.X) / block.W, (symCenter.Y - block.TopLeft.Y) / block.H

    let newCenter = 
        match scaleType with
            | ScaleUp ->
                {X = (block.TopLeft.X-5.) + ((block.W+10.) * xProp); Y = (block.TopLeft.Y-5.) + ((block.H+10.) * yProp)}
            | ScaleDown ->
                {X= (block.TopLeft.X+5.) + ((block.W-10.) * xProp); Y=  (block.TopLeft.Y+5.) + ((block.H-10.) * yProp)}

    let h,w = getRotatedHAndW sym
    let newPos = {X = (newCenter.X) - w/2.; Y= (newCenter.Y) - h/2.}
    let newComponent = { sym.Component with X = newPos.X; Y = newPos.Y}

    {sym with Pos = newPos; Component=newComponent; LabelHasDefaultPos=true}


/// <summary>HLP 23: AUTHOR Ismagilov - Scales symbol up or down. Scaling distance determined by 'mag' argument.</summary>
/// <param name="mag">  Positive scales up, negative scales down.</param>
/// <param name="block"> Bounding box of selected components</param>
/// <param name="sym"> Symbol to be rotated</param>
/// <returns>New symbol after scaled about block centre.</returns>
let scaleSymbolInBlockGroup
    (mag: float)
    //(scaleType: ScaleType)
    (block: BoundingBox)
    (sym: Symbol) : Symbol =

    let symCenter = getRotatedSymbolCentre sym

    //Get x and y proportion of symbol to block
    let xProp, yProp = (symCenter.X - block.TopLeft.X) / block.W, (symCenter.Y - block.TopLeft.Y) / block.H

    let newCenter = {X = (block.TopLeft.X-mag) + ((block.W+(mag*2.)) * xProp); Y = (block.TopLeft.Y-mag) + ((block.H+(mag*2.)) * yProp)}
        // match scaleType with
        //     | ScaleUp ->
        //         {X = (block.TopLeft.X-5.) + ((block.W+10.) * xProp); Y = (block.TopLeft.Y-5.) + ((block.H+10.) * yProp)}
        //     | ScaleDown ->
        //         {X= (block.TopLeft.X+5.) + ((block.W-10.) * xProp); Y=  (block.TopLeft.Y+5.) + ((block.H-10.) * yProp)}

    let h,w = getRotatedHAndW sym
    let newPos = {X = (newCenter.X) - w/2.; Y= (newCenter.Y) - h/2.}
    let newComponent = { sym.Component with X = newPos.X; Y = newPos.Y}

    {sym with Pos = newPos; Component=newComponent; LabelHasDefaultPos=true}





/// HLP 23: AUTHOR Klapper - Rotates a symbol based on a degree, including: ports and component parameters.

let rotateSymbolByDegree (degree: Rotation) (sym:Symbol)  =
    match degree with
    | Degree0 -> sym
    | Degree90 -> rotateSymbolInBlock RotateClockwise {X = sym.Component.X + sym.Component.W / 2.0 ; Y = sym.Component.Y + sym.Component.H / 2.0 } sym
    | Degree180 ->  rotateSymbolInBlock RotateClockwise {X = sym.Component.X + sym.Component.W / 2.0 ; Y = sym.Component.Y + sym.Component.H / 2.0 } sym
                    |> rotateSymbolInBlock RotateClockwise {X = sym.Component.X + sym.Component.W / 2.0 ; Y = sym.Component.Y + sym.Component.H / 2.0 } 
                    
                   
    | Degree270 -> rotateSymbolInBlock RotateAntiClockwise {X = sym.Component.X + sym.Component.W / 2.0 ; Y = sym.Component.Y + sym.Component.H / 2.0 } sym

///HLP 23: AUTHOR Klapper
///Generates automatic labels for wires
let labelNameSuggester (model : BusWireT.Model) (wire : Wire) =
    (getEndComponent model wire).Label + "," + string(model.Symbol.Ports[Symbol.getInputPortIdStr wire.InputPort].PortNumber)


///HLP 23: AUTHOR Klapper
///Creates popup for wire label
let wireReplacePopUp (model : BusWireT.Model) (wireId : ConnectionId)  =
    let wire = model.Wires[wireId]
    let title = "Replace selected wire with label"
    let suggestedName = labelNameSuggester model wire
    let beforeText =  $"How do you want to name your label? (Default: {suggestedName})" |> str
    let buttonAction = 
        fun (dispatch) -> 
            BusWireT.Msg.ReplaceWireWithLabel wireId |> dispatch
            BusWireT.Msg.ClosePopup |> dispatch
    let buttonText = "Replace"
    
    let myPopup = popupWithTextInputAndTwoButtonsFunc title beforeText "Label name here" buttonAction buttonText
    {model with PopupViewFunc = Some myPopup}

///HLP 23: AUTHOR Klapper
///Creates popup for bunchwire replace 
let wireReplaceAllPopup (model : BusWireT.Model) (wireIdList : ConnectionId list) =
    let title = "Replace all the selected wires with labels"
    let text = "Do you want to replace all selected/unroutable wires with labels?" |> str
    let buttonAction =
        fun dispatch ->
            BusWireT.Msg.ReplaceWireListWithLabels wireIdList |> dispatch
            BusWireT.Msg.ClosePopup |> dispatch
    let buttonText = "Yes"
    let myPopup = popupWithTwoButtonsFunc title text buttonAction buttonText
    {model with PopupViewFunc = Some myPopup}


let createLabelSymbol position label model =
    let labelComponent = makeComponent position IOLabel (uuid()) label
    { 
        Pos = position
        LabelBoundingBox = {TopLeft=position; W=0.;H=0.} // dummy, will be replaced
        LabelHasDefaultPos = true
        LabelRotation = None
        Appearance =
            {
                HighlightLabel = false
                ShowPorts = ShowNone
                Colour = "rgb(120,120,120)"
                Opacity = 1.0
                Style = model.Symbol.Style
            }
        InWidth0 = None // set by BusWire
        InWidth1 = None
        Id = ComponentId labelComponent.Id
        Component = labelComponent
        Moving = false
        PortMaps = Symbol.initPortOrientation labelComponent
        STransform = {Rotation = Degree0; flipped = false}
        ReversedInputPorts = Some false
        MovingPort = None
        IsClocked = false
        MovingPortTarget = None
        HScale = None
        VScale = None
        }
        |> Symbol.autoScaleHAndW

/// HLP 23: AUTHOR Klapper - 
/// Function which replaces a wire with two labels directly connected to original inputs and outputs of the wire. 
let replaceWireWithLabelWithName (model : DrawModelType.BusWireT.Model) (wireId : ConnectionId) (label : string) =
    let wire = model.Wires[wireId]

    let startComp = getStartComponent model wire
    let startOrigin = Symbol.getOutputPortLocation None model.Symbol wire.OutputPort 
    let (_, _, compHeight, compWidth) = getComponentProperties ComponentType.IOLabel ""
    let  startPos, startRotation =
        match model.Symbol.Symbols[ComponentId startComp.Id].PortMaps.Orientation[Symbol.getOutputPortIdStr wire.OutputPort] with
        | Edge.Top ->  {X = startOrigin.X - compWidth / 2.0; Y = startOrigin.Y - 20.0}, Degree270
        | Edge.Bottom ->  {X = startOrigin.X - compWidth / 2.0; Y = startOrigin.Y + 20.0}, Degree90
        | Edge.Left ->  {X = startOrigin.X - 60.0; Y =  startOrigin.Y - compHeight / 2.0}, Degree180
        | Edge.Right -> {X = startOrigin.X + 20.0; Y =  startOrigin.Y - compHeight / 2.0},Degree0

    let endComp = getEndComponent model wire
    let endOrigin = Symbol.getInputPortLocation None model.Symbol wire.InputPort 
    let  endPos, endRotation = 
        match model.Symbol.Symbols[ComponentId endComp.Id].PortMaps.Orientation[Symbol.getInputPortIdStr wire.InputPort] with
        | Edge.Top ->  {X = endOrigin.X - compWidth / 2.0; Y = endOrigin.Y - 60.0}, Degree90
        | Edge.Bottom ->  {X = endOrigin.X - compWidth / 2.0; Y = endOrigin.Y + 20.0}, Degree270
        | Edge.Left ->  {X = endOrigin.X - 60.0; Y =  endOrigin.Y - compHeight / 2.0},Degree0    
        | Edge.Right -> {X = endOrigin.X + 20.0; Y =  endOrigin.Y - compHeight / 2.0} , Degree180

    let startSymbol = createLabelSymbol startPos label model 
                    |> rotateSymbolByDegree startRotation |> Symbol.autoScaleHAndW
    let endSymbol = createLabelSymbol endPos label model 
                    |> rotateSymbolByDegree endRotation 
    let symbolModel = model.Symbol
    let startSymbolMap = symbolModel.Symbols.Add (startSymbol.Id, startSymbol) 
    let startNewPorts = Symbol.addToPortModel symbolModel startSymbol 
    let endSymbolMap = {symbolModel with Symbols = startSymbolMap; Ports = startNewPorts}.Symbols.Add (endSymbol.Id, endSymbol) 
    let endNewPorts = Symbol.addToPortModel {symbolModel with Symbols = endSymbolMap; Ports = startNewPorts} endSymbol 

    let newModel = {model with Symbol = {symbolModel with Symbols = endSymbolMap; Ports = endNewPorts}}

    let startLabelInputPortPos  = Symbol.getInputPortLocation None newModel.Symbol (InputPortId startSymbol.Component.InputPorts[0].Id)
    let endLabelOutputPortPos = Symbol.getOutputPortLocation None newModel.Symbol (OutputPortId endSymbol.Component.OutputPorts[0].Id)

    let startSegmentList = makeInitialSegmentsList wire.WId wire.StartPos startLabelInputPortPos Edge.Left
    let endWireId = JSHelpers.uuid ()
    let endSegmentList = makeInitialSegmentsList (ConnectionId endWireId) endLabelOutputPortPos wire.EndPos Edge.Right
    let newStartWire = {wire with InputPort = InputPortId startSymbol.Component.InputPorts[0].Id; Segments = startSegmentList} |> autoroute newModel 

    let newEndWire = {wire with OutputPort = OutputPortId endSymbol.Component.OutputPorts[0].Id; Segments = endSegmentList; WId = ConnectionId endWireId} |> autoroute newModel

    [newStartWire; newEndWire] |>  updateModelWires newModel

///HLP23 AUTHOR: Klapper. Replaces a wire with a label with an auto generated name
let replaceWireWithLabel (model : DrawModelType.BusWireT.Model) (wire : Wire) =
    labelNameSuggester model wire
    |> replaceWireWithLabelWithName model wire.WId

///HLP 23: AUTHOR Rzepala
///this function return common wires between two adjacent symbols
///for example for the given configuration of two symbols:
///
/// ------------
/// |          |
/// |          |
/// |          |        ---------------------------
/// |          |        |                         |
/// |          |        |                         |
/// ------------        |                         |
///                     |                         |
///                     ---------------------------
///
/// it should return all the wires between the right side of the
/// left symbol and the left side of the right symbol
let getCommonWires 
    (wModel: BusWireT.Model)
    (symbolToResize: Symbol)
    (otherSymbol: Symbol)
    (orient: OrientationS option)
        : Map<ConnectionId, Wire> =
    let manageableWires = Map.toList wModel.Wires |> List.map (fun (_, x) -> x)

    match orient with
    | Some TopBottom ->
        if symbolToResize.Pos.Y < otherSymbol.Pos.Y then 
            manageableWires 
            |> List.filter (fun x -> 
                (List.contains (string x.InputPort) (Map.find Edge.Bottom symbolToResize.PortMaps.Order) 
                || List.contains (string x.OutputPort) (Map.find Edge.Bottom symbolToResize.PortMaps.Order))
                && (List.contains (string x.InputPort) (Map.find Edge.Top otherSymbol.PortMaps.Order) 
                || List.contains (string x.OutputPort) (Map.find Edge.Top otherSymbol.PortMaps.Order)))
            |> List.map (fun c -> (c.WId, c))
            |> Map.ofList
        else 
            manageableWires 
            |> List.filter (fun x -> 
                (List.contains (string x.InputPort) (Map.find Edge.Top symbolToResize.PortMaps.Order) 
                || List.contains (string x.OutputPort) (Map.find Edge.Top symbolToResize.PortMaps.Order))
                && (List.contains (string x.InputPort) (Map.find Edge.Bottom otherSymbol.PortMaps.Order) 
                || List.contains (string x.OutputPort) (Map.find Edge.Bottom otherSymbol.PortMaps.Order)))
            |> List.map (fun c -> (c.WId, c))
            |> Map.ofList
    | Some LeftRight->
        if symbolToResize.Pos.X > otherSymbol.Pos.X then 
            manageableWires 
            |> List.filter (fun x -> 
                (List.contains (string x.InputPort) (Map.find Edge.Left symbolToResize.PortMaps.Order) 
                || List.contains (string x.OutputPort) (Map.find Edge.Left symbolToResize.PortMaps.Order))
                && (List.contains (string x.InputPort) (Map.find Edge.Right otherSymbol.PortMaps.Order) 
                || List.contains (string x.OutputPort) (Map.find Edge.Right otherSymbol.PortMaps.Order)))
            |> List.map (fun c -> (c.WId, c))
            |> Map.ofList
        else 
            manageableWires 
            |> List.filter (fun x -> 
                (List.contains (string x.InputPort) (Map.find Edge.Right symbolToResize.PortMaps.Order) 
                || List.contains (string x.OutputPort) (Map.find Edge.Right symbolToResize.PortMaps.Order))
                && (List.contains (string x.InputPort) (Map.find Edge.Left otherSymbol.PortMaps.Order) 
                || List.contains (string x.OutputPort) (Map.find Edge.Left otherSymbol.PortMaps.Order)))
            |> List.map (fun c -> (c.WId, c))
            |> Map.ofList
    | None -> failwithf "Whatt?"


///HLP 23: AUTHOR Rzepala
/// return all the ports from a given edge of the symbol in order (left->right) or (up->down)
let getAllPortsFromEdgeOrdered
    (wModel: BusWireT.Model)
    (symbol: Symbol)
    (orient: OrientationS option)
    (edge: Edge)
        : (string * XYPos) list =
    let edgePorts = Map.find edge symbol.PortMaps.Order
    wModel.Symbol.Ports 
    |> Map.toList
    |> List.filter (fun (x, _) -> List.contains x edgePorts)
    |> List.map (fun (y, x) -> y, (getPortPos symbol x))
    |> List.sortBy (fun (_, x) -> 
                        match orient with
                        | Some TopBottom -> x.X
                        | Some LeftRight -> x.Y
                        | None -> failwithf "Whatt?")
    |> List.map (fun (x, y) -> (x, (y + symbol.Pos)))

///HLP 23: AUTHOR Rzepala
///this function checks if two straight lines overlap 
///for example if two horizontal lines, at different or same heights, overlap, that means 
///we can draw a horizontal lines that will intersect both of them
let isOverlapped
    (firstBegin: float)
    (firstEnd: float)
    (secondBegin: float)
    (secondEnd: float)
        : bool =
    if firstBegin > secondBegin && firstBegin < secondEnd
    then true
    elif firstEnd < secondEnd && firstEnd > secondBegin
    then true
    elif secondBegin > firstBegin && secondBegin < firstEnd
    then true
    elif secondEnd < firstEnd && secondEnd > firstBegin
    then true
    else false

// HLP 23: AUTHOR Khoury & Ismagilov
// Function taken from Symbol.fs and modified to not use loaded components 
/// Creates a new symbol used for the button symbol because it does not take loaded components as an argument
let createNewSymbolButton  (pos: XYPos) (comptype: ComponentType) (label:string) modelStyle =
    let id = JSHelpers.uuid ()
    let comp = makeComponent pos comptype id label
    let transform = {Rotation= Degree0; flipped= false}

    { 
      Pos = { X = pos.X - float comp.W / 2.0; Y = pos.Y - float comp.H / 2.0 }
      LabelBoundingBox = {TopLeft=pos; W=0.;H=0.} // dummy, will be replaced
      LabelHasDefaultPos = false
      LabelRotation = None
      Appearance =
          {
            HighlightLabel = false
            ShowPorts = ShowNone
            Colour = "Black"
            Opacity = 1.0
            Style = modelStyle
          }
      InWidth0 = None // set by BusWire
      InWidth1 = None
      Id = ComponentId id
      Component = comp
      Moving = false
      PortMaps = initPortOrientation comp
      STransform = transform
      ReversedInputPorts = Some false
      MovingPort = None
      IsClocked = false
      MovingPortTarget = None
      HScale = None
      VScale = None
      
    }
    |> autoScaleHAndW
    |> calcLabelBoundingBox


///HLP23 AUTHOR: Klapper & Rzepala
///Returns the center point of the component
let getComponentCenter  (compId : Symbol)  =
    let comp = compId.Component
    {X = comp.X + comp.W / 2.0; Y = comp.Y + comp.H / 2.0}

///HLP23 AUTHOR: Klapper & Rzepala
///Returns the distance between the two center points of the component
let checkDistanceComponent (compId1 : Symbol) (compId2 : Symbol) =
    euclideanDistance (getComponentCenter compId1) (getComponentCenter compId2)


///HLP23 AUTHOR: Klapper & Rzepala
///Returns an option aligment of the two components
let checkCompAlignment (model : BusWireT.Model) (compId1 : ComponentId) (compId2 : ComponentId) = 
    let comp1 = model.Symbol.Symbols[compId1].Component
    let comp2 = model.Symbol.Symbols[compId2].Component
    match isOverlapped comp1.X (comp1.X + comp1.W) comp2.X (comp2.X + comp2.W),
          isOverlapped comp1.Y (comp1.Y + comp1.H) comp2.Y (comp2.Y + comp2.H) with
    | true, false -> Some LeftRight
    | false, true -> Some TopBottom
    | _, _ -> None


///HLP23 AUTHOR: Klapper & Rzepala
///Returns a symbol which is closest to the selected symbol (by their centres)
let getDistanceAlignments 
    (compId : Symbol) 
    (lstId: List<Symbol>) 
        : float*Symbol =
    let lstId' = lstId |> List.filter (fun x -> compId <> x)
    let mapper compare =
        checkDistanceComponent  compId compare,
        compare
    lstId' 
    |> List.map mapper
    |> List.minBy (fun (x, _) -> x)


///HLP23 AUTHOR: Rzepala
///Returns the center point of the component
let getComponentCenterMap (model: SymbolT.Model) (compId : ComponentId)  =
    let symbolMap = model.Symbols |> Map.find compId
    let comp = symbolMap.Component
    {X = comp.X + comp.W / 2.0; Y = comp.Y + comp.H / 2.0}

///HLP23 AUTHOR: Rzepala
///Returns the distance between the two center points of the component
let checkDistanceComponentMap (model: SymbolT.Model) (compId1 : ComponentId) (compId2 : ComponentId) =
    euclideanDistance (getComponentCenterMap model compId1) (getComponentCenterMap model compId2)
///HLP23 AUTHOR: Rzepala
///Returns a symbol which is closest to the selected symbol (by their centres)
let getDistanceAlignmentsMap 
    (model: SymbolT.Model)
    (compId : ComponentId) 
    (lstId: Map<ComponentId, Symbol>) 
        : ComponentId =

    let lstId' = 
        lstId 
        |> Map.toList
        |> List.filter (fun (x, _) -> compId <> x)
        |> List.map (fun (x, _) -> x)
    let mapper compare =
        checkDistanceComponentMap  model compId compare,
        compare
    lstId' 
    |> List.map mapper
    |> List.minBy (fun (x, _) -> x)
    |> snd