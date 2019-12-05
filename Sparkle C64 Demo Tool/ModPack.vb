﻿Friend Module ModPack

	Private FirstBlockOfNextFile As Boolean = False 'If true, this is the first block of next file in same buffer, Lit Selector Bit NOT NEEEDED
	Private NextFileInBuffer As Boolean = False     'Indicates whether the next file is added to the same buffer

	Private BlockUnderIO As Integer = 0
	Private AdLoPos As Byte, AdHiPos As Byte

	'Match offset and length is 1 based
	Private ReadOnly MaxOffset As Integer = 255 + 1 'Offset will be decreased by 1 when saved
	Private ReadOnly ShortOffset As Integer = 63 + 1

	Private ReadOnly MaxLongLen As Byte = 254 + 1   'Cannot be 255, there is an INY in the decompression ASM code, and that would make YR=#$00
	Private ReadOnly MaxMidLen As Byte = 61 + 1     'Cannot be more than 61 because 62=LongMatchTag, 63=NextFileTage
	Private ReadOnly MaxShortLen As Byte = 3 + 1    '1-3, cannot be 0 because it is preserved for EndTag

	'Literal length is 0 based
	Private ReadOnly MaxLitLen As Integer = 1 + 4 + 8 + 32 - 1  '=44 - this seems to be optimal, 1+4+8+16 and 1+4+8+64 are worse...

	Private MatchBytes As Integer = 0
	Private MatchBits As Integer = 0
	Private LitBits As Integer = 0
	Private MLen As Integer = 0
	Private MOff As Integer = 0

	Structure Sequence
		'Public Nxt As Integer           'Next Sequence element's index - may not be needed
		Public Len As Integer           'Length of the sequence in bytes (0 based)
		Public Off As Integer           'Offset of Match sequence in bytes (1 based), 0 if Literal Sequence
		Public Bit As Integer           'Total Bits in Buffer
	End Structure

	Private Seq() As Sequence           'Sequence array, to find the best sequence
	Private SL(), SO(), LL(), LO() As Integer
	Private SI As Integer               'Sequence array index
	Private StartPos As Integer
	Private TmpSI1, TmpSI2 As Integer
	Private LastBlockDone As Boolean = False
	Private ReadOnly LastBlockCheck As Boolean = True

	Public Sub PackFile(PN As Byte(), Optional FA As String = "", Optional FUIO As Boolean = False)

		'----------------------------------------------------------------------------------------------------------
		'PROCESS FILE
		'----------------------------------------------------------------------------------------------------------
		TmpSI1 = 0
		TmpSI2 = 0

		Prg = PN
		FileUnderIO = FUIO
		PrgAdd = Convert.ToInt32(FA, 16)
		PrgLen = Prg.Length     'Convert.ToInt32(FL,16)

		ReDim SL(PrgLen - 1), SO(PrgLen - 1), LL(PrgLen - 1), LO(PrgLen - 1)
		ReDim Seq(PrgLen)           'This is actually one element more in the array, to have starter element with 0 values

		'----------------------------------------------------------------------------------------------------------
		'CALCULATE BEST SEQUENCE
		'----------------------------------------------------------------------------------------------------------
		CalcSequence(PrgLen - 1, 1)

		'----------------------------------------------------------------------------------------------------------
		'DETECT BUFFER STATUS AND INITIALIZE COMPRESSION
		'----------------------------------------------------------------------------------------------------------

		If ((BufferCnt = 0) And (ByteCnt = 254)) Or (ByteCnt = 255) Then
			FirstBlockOfNextFile = False                            'First block in buffer, Lit Selector Bit is needed (will be compression bit)
			NextFileInBuffer = False                                'This is the first file that is being added to an empty buffer
		Else
			FirstBlockOfNextFile = True                             'First block of next file in same buffer, Lit Selector Bit NOT NEEEDED
			NextFileInBuffer = True                                 'Next file is being added to buffer that already has data
		End If

		If NewPart Then
			BlockPtr = ByteSt.Count + 255                           'If this is a new part, store Block Counter Pointer
			NewPart = False
		End If

		Buffer(ByteCnt) = (PrgAdd + PrgLen - 1) Mod 256             'Add Address Hi Byte
		AdLoPos = ByteCnt

		If CheckIO(PrgLen - 1) = 1 Then                             'Check if last byte of block is under IO or in ZP
			BlockUnderIO = 1                                        'Yes, set BUIO flag
			ByteCnt -= 1                                            'And skip 1 byte (=0) for IO Flag
		Else
			BlockUnderIO = 0
		End If

		Buffer(ByteCnt - 1) = Int((PrgAdd + PrgLen - 1) / 256)      'Add Address Lo Byte
		AdHiPos = ByteCnt - 1

		ByteCnt -= 2
		'LitCnt = -1                                                 'Reset LitCnt here
		LastByte = ByteCnt                       'The first byte of the ByteStream after (BlockCnt and IO Flag and) Address Bytes (251..253)

		'----------------------------------------------------------------------------------------------------------
		'COMPRESS FILE
		'----------------------------------------------------------------------------------------------------------

		Pack()

	End Sub

	Private Sub CalcSequence(SeqStart As Integer, SeqEnd As Integer)

		Dim MaxO, MaxL As Integer
		Dim SeqLen, SeqOff As Integer
		Dim LeastBits As Integer

		'Pos = Max to Min>0 value
		For Pos As Integer = SeqStart To SeqEnd Step -1  'Pos cannot be 0, Prg(0) is always literal as it is always 1 byte left
			SO(Pos) = 0
			SL(Pos) = 0
			LO(Pos) = 0
			LL(Pos) = 0
			'Offset goes from 1 to max offset (cannot be 0)
			MaxO = IIf(Pos + MaxOffset < SeqStart, MaxOffset, SeqStart - Pos)
			'Match length goes from 1 to max length
			MaxL = IIf(Pos >= MaxLongLen, MaxLongLen, Pos)  'MaxL=254 or less
			For O As Integer = 1 To MaxO                                    'O=1 to 255 or less
				'Check if first byte matches at offset, if not go to next offset
				If Prg(Pos) = Prg(Pos + O) Then
					For L As Integer = 1 To MaxL                            'L=1 to 254 or less
						If L = MaxL Then
							'L += 1
							GoTo Match
						ElseIf Prg(Pos - L) <> Prg(Pos + O - L) Then
							'L=MatchLength + 1 here
							If L >= 2 Then
Match:                          If O <= ShortOffset Then
									If (SL(Pos) < MaxShortLen) And (SL(Pos) < L) Then
										SL(Pos) = IIf(L > MaxShortLen, MaxShortLen, L)
										SO(Pos) = O       'Keep O 1-based
									End If
									If L > LL(Pos) Then
										LL(Pos) = L
										LO(Pos) = O
									End If
								Else
									If (L > LL(Pos)) And (L > 2) Then 'Skip short (2-byte) MidMatches
										LL(Pos) = L
										LO(Pos) = O
									End If
								End If
							End If
							Exit For
						End If
					Next
					'If both short and long matches maxed out, we can leave the loop and go to the next Prg position
					If (LL(Pos) = IIf(Pos >= MaxLongLen, MaxLongLen, Pos)) And
						(SL(Pos) = IIf(Pos >= MaxShortLen, MaxShortLen, Pos)) Then
						Exit For
					End If
				End If
			Next
		Next

		'Dim S As String = ""
		'For I As Integer = SeqStart To 0 Step -1
		'S += I.ToString + vbTab + LO(I).ToString + vbTab + LL(I).ToString + vbTab + SO(I).ToString + vbTab + SL(I).ToString + vbNewLine
		'Next
		'
		'IO.File.WriteAllText(UserFolder + "\Onedrive\c64\Coding\Seq\OffLen " + Hex(Prg.Length) + "-" + Hex(SeqStart) + ".txt", S)

		LitCnt = 0             'Reset LitCnt: we start with 1 literal (LitCnt is 0 based)

		With Seq(1)             'Initialize first element of sequence
			.Len = LitCnt      '1 Literal byte, Len is 0 based
			.Off = 0            'Offset=0 -> literal sequence, Off is 1 based
			'.Nxt = 0            'Last element in sequence
			.Bit = 10           'LitLen bit + 8 bits + type (Lit vs Match) selector bit 
		End With

		For Pos As Integer = SeqEnd To SeqStart    'Start with second element, first has been initialized  above
			LeastBits = &HFFFFFF                     'Max block size=100 = $10000 bytes = $80000 bits, make default larger than this

			If LL(Pos) <> 0 Then
				SeqLen = LL(Pos)
			ElseIf SL(Pos) <> 0 Then
				SeqLen = SL(Pos)
			Else
				'Increase literal cnt
				GoTo Literals                   'Both LL(Pos) and SL(Pos) are 0, this is a literal byte
			End If

			'Check all possible lengths
			For L As Integer = SeqLen To 2 Step -1
				'For L As Integer = SeqLen To IIf(SeqLen - 2 > 2, SeqLen - 2, 2) Step -1
				'Get offset, use short match if possible
				SeqOff = IIf(L <= SL(Pos), SO(Pos), LO(Pos))
				'Calculate MatchBits
				CalcMatchBits(SeqLen, SeqOff)

				'See if total bit count is better than best version
				If Seq(Pos + 1 - L).Bit + MatchBits < LeastBits Then
					'If better, update best version
					LeastBits = Seq(Pos + 1 - L).Bit + MatchBits
					'and save it to sequence at Pos+1 (position is 1 based)
					With Seq(Pos + 1)
						.Len = L            'MatchLen is 1 based
						.Off = SeqOff       'Off is 1 based
						'.Nxt = Pos + 1 - L
						.Bit = LeastBits
					End With
				End If
			Next

Literals:
			'Continue previous Lit sequence or start new sequence
			LitCnt = If(Seq(Pos).Off = 0, Seq(Pos).Len, -1)

			'Calculate literal bits for a presumtive LitCnt+1 value
			CalcLitBits(LitCnt + 1)             'This updates LitBits
			LitBits += (LitCnt + 2) * 8         'Lit Bits + Lit Bytes
			'See if total bit count is less than best version
			If Seq(Pos - LitCnt - 1).Bit + LitBits < LeastBits Then  '=Seq(Pos + 1 - (LitCnt + 1)) simplified
				'If better, update best version
				LeastBits = Seq(Pos - LitCnt - 1).Bit + LitBits  '=Seq(Pos + 1 - (LitCnt + 1)) simplified
				'and save it to sequence at Pos+1 (position is 1 based)
				With Seq(Pos + 1)
					.Len = LitCnt + 1       'LitCnt is 0 based, LitLen is 0 based
					.Off = 0            'An offset of 0 marks a literal sequence, match offset is 1 based
					'.Nxt = Pos - LitCnt '= Pos + 1 - (LitCnt + 1) simplified
					.Bit = LeastBits
				End With
			End If

		Next

		'S = ""
		'For I As Integer = SeqStart To 0 Step -1
		'S += I.ToString + vbTab + Seq(I + 1).Off.ToString + vbTab + Seq(I + 1).Len.ToString + vbTab + Seq(I + 1).Bit.ToString + vbNewLine
		'Next
		'IO.File.WriteAllText(UserFolder + "\Onedrive\c64\Coding\Seq\Seq " + Hex(Prg.Length) + "-" + Hex(SeqStart) + ".txt", S)

	End Sub

	Private Sub Pack()
		Dim BufferFull As Boolean

		LastBlockDone = False

		SI = PrgLen - 1
		StartPos = SI

Restart:
		Do

			If Seq(SI + 1).Off = 0 Then
				'--------------------------------------------------------------------
				'Literal sequence
				'--------------------------------------------------------------------
				LitCnt = Seq(SI + 1).Len                'LitCnt is 0 based
				MLen = 0                                'Reset MLen - this is needed for accurate bit counting in SequenceFits

				BufferFull = False
				Do While LitCnt > -1
					If SequenceFits(LitCnt + 1, CalcLitBits(LitCnt), LitCnt, CheckIO(SI - LitCnt)) = True Then
						AddLitBytes(LitCnt)
						Exit Do
					End If
					LitCnt -= 1
					BufferFull = True
				Loop

				'Go to next element in sequence
				SI -= LitCnt + 1    'If nothing added to the buffer, LitCnt=-1+1=0

				If BufferFull = True Then
					AddLitBits()    'Add literal bits of the last literal sequence
					CloseBuffer()   'The whole literal sequence did not fit, buffer is full, close it
				End If

			Else
				'--------------------------------------------------------------------
				'Match sequence
				'--------------------------------------------------------------------
				BufferFull = False

				MLen = Seq(SI + 1).Len      '1 based
				MOff = Seq(SI + 1).Off      '1 based

				CalcMatchBits(MLen, MOff)
				If MatchBytes = 3 Then
					'--------------------------------------------------------------------
					'Long Match
					'--------------------------------------------------------------------
					If SequenceFits(3, CalcLitBits(LitCnt), LitCnt, CheckIO(SI - MLen + 1)) Then
						AddLitBits()
						'Add long match
						AddLongMatch()
					Else
						MLen = MaxMidLen
						BufferFull = True   'Buffer if full, we will need to close it
						GoTo CheckMid
					End If
				ElseIf MatchBytes = 2 Then
					'--------------------------------------------------------------------
					'Mid Match
					'--------------------------------------------------------------------
CheckMid:           If SequenceFits(2, CalcLitBits(LitCnt), LitCnt, CheckIO(SI - MLen + 1)) Then
						AddLitBits()
						'Add mid match
						AddMidMatch()
					Else
						BufferFull = True
						If SO(SI) <> 0 Then
							MLen = SL(SI)   'SL and SO array indeces are 0 based
							MOff = SO(SI)
							GoTo CheckShort
						Else
							GoTo CheckLit
						End If  'Short vs Literal
					End If      'Mid vs Short
				Else
					'--------------------------------------------------------------------
					'Short Match
					'--------------------------------------------------------------------
CheckShort:         If SequenceFits(1, CalcLitBits(LitCnt), LitCnt, CheckIO(SI - MLen + 1)) Then
						AddLitBits()
						'Add short match
						AddShortMatch()
					Else
						'--------------------------------------------------------------------
						'Match does not fit, check if 1 literal byte fits
						'--------------------------------------------------------------------
						BufferFull = True
CheckLit:               MLen = 1    'This is needed here for accurate Bit count calculation in SequenceFits (indicates Literal, not Match)
						If SequenceFits(1, CalcLitBits(LitCnt + 1), LitCnt + 1, CheckIO(SI - LitCnt)) Then
							'MLen = 1        '1 based
							LitCnt += 1     '0 based
							AddLitBits()
							AddLitBytes(0)   'Add 1 literal byte (the rest has been added previously)
						Else
							'Nothing fits
							MLen = 0
						End If  'Literal vs nothing
					End If      'Short match vs literal
				End If          'Long, mid, or short match
Done:
				SI -= MLen

				If BufferFull Then
					AddLitBits()
					CloseBuffer()
				End If
			End If              'Lit vs match

		Loop While SI >= 0

		AddLitBits()            'See if any literal bits need to be added, space has been previously reserved for them

	End Sub

	Private Function CalcMatchBits(Length As Integer, Offset As Integer) As Integer 'Match Length is 1 based

		If (Length <= MaxShortLen) And (Offset <= ShortOffset) Then
			MatchBytes = 1
			MatchBits = 8 + 1       '1 match byte + 1 type selector bit AFTER match sequence
		ElseIf Length <= MaxMidLen Then
			MatchBytes = 2
			MatchBits = 16 + 1      '2 match bytes + 1 type selector bit AFTER match sequence
		Else
			MatchBytes = 3
			MatchBits = 24 + 1      '3 match bytes + 1 type selector bit AFTER match sequence
		End If

		CalcMatchBits = MatchBits

	End Function

	Private Function CalcLitBits(Lits As Integer) As Integer     'LitCnt is 0 based

		If Lits = -1 Then
			CalcLitBits = 0
		Else
			CalcLitBits = Fix(Lits / (MaxLitLen + 1)) * 9

			Select Case Lits Mod (MaxLitLen + 1)
				Case 0
					CalcLitBits += 1 + 0           '1	1 bittab bit
				Case 1 To 4
					CalcLitBits += 2 + 2 + 0       '4	2 bittab bits + 2 lit sequence length bits
				Case 5 To 12
					CalcLitBits += 3 + 3 + 0       '6	3 bittab bits + 3 lit sequence length bits
				Case 13 To MaxLitLen - 1
					CalcLitBits += 3 + 5 + 0       '8	3 bittab bits + 5 lit sequence length bits
				Case MaxLitLen
					CalcLitBits += 3 + 5 + 1       '9	3 bittab bits + 5 lit sequence length bits + 1 type selector bit AFTER lit sequence
			End Select
		End If

		LitBits = CalcLitBits

	End Function


	Private Function SequenceFits(BytesToAdd As Integer, BitsToAdd As Integer, Literals As Integer, Optional SequenceUnderIO As Integer = 0) As Boolean
		On Error GoTo Err

		'Calculate total bit count in buffer from ByteCnt, BitCnt, and BitPos
		Dim BitsInBuffer As Integer = ((255 - ByteCnt) * 8) + (BitCnt * 8) + 16 - BitPos

		'Add close byte
		BytesToAdd += 1

		'Add a close bit (match tag for close byte) only if the last sequence to be added is a match
		'Say we are trying ot add a match here. If this is the last sequence that fits in the buffer
		'The it will need to be followed by a close match bit and a close byte
		'This is the close match bit
		If MLen > 1 Then BitsToAdd += 1     'We know we are tryin to add a match sequence here if MLen>1

		'Check if we have pending Literals
		'If no pending Literals, or Literals=MaxLit, then we need to save 1 bit for match tag
		'Otherwise, next item must be a match, we do not need a match tag
		If (Literals = -1) Or (Literals Mod (MaxLitLen + 1) = MaxLitLen) Then BitsToAdd += 1

		If (BlockUnderIO = 0) And (SequenceUnderIO = 1) Then
			'Some blocks of the file will go UIO, but sofar this block is not UIO, and next byte is the first one that goes UIO
			BytesToAdd += 1    'Need an extra byte if the next byte in sequence is the first one that goes UIO
		End If

		If BitsInBuffer + (BytesToAdd * 8) + BitsToAdd <= 2048 Then
			SequenceFits = True
			'Data will fit
			If (BlockUnderIO = 0) And (SequenceUnderIO = 1) And (LastByte <> ByteCnt) Then

				'This is the first byte in the block that will go UIO, so lets update the buffer to include the IO flag

				For I As Integer = ByteCnt To AdHiPos            'Move all data to the left in buffer, including AdHi
					Buffer(I - 1) = Buffer(I)
				Next
				Buffer(AdHiPos) = 0                             'IO Flag to previous AdHi Position
				ByteCnt -= 1                                    'Update ByteCt to next empty position in buffer
				LastByteCt -= 1                                 'Last Match pointer also needs to be updated (BUG FIX - REPORTED BY RAISTLIN/G*P)
				AdHiPos -= 1                                    'Update AdHi Position in Buffer
				BlockUnderIO = 1                                'Set BlockUnderIO Flag
			End If
		Else
			SequenceFits = False
		End If

		Exit Function
Err:
		MsgBox(ErrorToString(), vbOKOnly + vbExclamation, Reflection.MethodBase.GetCurrentMethod.Name + " Error")

		SequenceFits = False

	End Function
	Private Sub AddLongMatch()

		If (LitCnt = -1) Or (LitCnt Mod (MaxLitLen + 1) = MaxLitLen) Then AddRBits(0, 1)   '0		Last Literal Length was -1 or Max, we need the Match Tag

		Buffer(ByteCnt) = LongMatchTag                   'Long Match Flag = &HF8
		Buffer(ByteCnt - 1) = MLen - 1
		Buffer(ByteCnt - 2) = MOff - 1
		ByteCnt -= 3

		LitCnt = -1

		Exit Sub
Err:
		MsgBox(ErrorToString(), vbOKOnly + vbExclamation, Reflection.MethodBase.GetCurrentMethod.Name + " Error")

	End Sub

	Private Sub AddMidMatch()
		On Error GoTo Err

		If (LitCnt = -1) Or (LitCnt Mod (MaxLitLen + 1) = MaxLitLen) Then AddRBits(0, 1)   '0		Last Literal Length was -1 or Max, we need the Match Tag

		Buffer(ByteCnt) = (MLen - 1) * 4                         'Length of match (#$02-#$3f, cannot be #$00 (end byte), and #$01 - distant selector??)
		Buffer(ByteCnt - 1) = MOff - 1
		ByteCnt -= 2

		LitCnt = -1

		Exit Sub
Err:
		MsgBox(ErrorToString(), vbOKOnly + vbExclamation, Reflection.MethodBase.GetCurrentMethod.Name + " Error")

	End Sub

	Private Sub AddShortMatch()
		On Error GoTo Err

		If (LitCnt = -1) Or (LitCnt Mod (MaxLitLen + 1) = MaxLitLen) Then AddRBits(0, 1)   '0		Last Literal Length was -1 or Max, we need the Match Tag

		Buffer(ByteCnt) = ((MOff - 1) * 4) + (MLen - 1)
		ByteCnt -= 1

		LitCnt = -1

		Exit Sub
Err:
		MsgBox(ErrorToString(), vbOKOnly + vbExclamation, Reflection.MethodBase.GetCurrentMethod.Name + " Error")

	End Sub

	Private Sub AddLitBytes(Lits As Integer)
		'On Error GoTo Err

		For I As Integer = 0 To Lits
			Buffer(ByteCnt) = Prg(SI - I)   'Add byte to Byte Stream
			ByteCnt -= 1                    'Update Byte Position Counter
		Next I

		Exit Sub
Err:
		MsgBox(ErrorToString(), vbOKOnly + vbExclamation, Reflection.MethodBase.GetCurrentMethod.Name + " Error")

	End Sub

	Private Sub AddLitBits()
		On Error GoTo Err

		If LitCnt = -1 Then Exit Sub    'We only call this routine with LitCnt>-1

		For I As Integer = 1 To Fix(LitCnt / (MaxLitLen + 1))
			If FirstBlockOfNextFile = True Then
				AddRBits(&B11111111, 8)
				FirstBlockOfNextFile = False
			Else
				AddRBits(&B111111111, 9)
			End If
		Next

		If FirstBlockOfNextFile = False Then
			AddRBits(1, 1)               'Add Literal Selector if this is not the first (Literal) byte in the buffer
		Else
			FirstBlockOfNextFile = False
		End If

		Dim Lits As Integer = LitCnt Mod (MaxLitLen + 1)

		Select Case Lits
			Case 0
				AddRBits(0, 1)              'Add Literal Length Selector 0	- read no more bits
			Case 1 To 4
				AddRBits(2, 2)              'Add Literal Length Selector 10 - read 2 more bits
				AddRBits(Lits - 1, 2)       'Add Literal Length: 00-03, 2 bits	-> 1000 00xx when read
			Case 5 To 12
				AddRBits(6, 3)              'Add Literal Length Selector 110 - read 3 more bits
				AddRBits(Lits - 5, 3)       'Add Literal Length: 00-07, 3 bits	-> 1000 1xxx when read
			Case 13 To MaxLitLen - 1
				AddRBits(7, 3)              'Add Literal Length Selector 111 - read 5 more bits
				AddRBits(Lits - 13, 5)      'Add Literal Length: 00-1f, 5 bits	-> 101x xxxx when read
			Case MaxLitLen
				AddRBits(7, 3)              'Add Literal Length Selector 111 - read 5 more bits
				AddRBits(Lits - 13, 5)      'Add Literal Length: 00-1f, 5 bits	-> 101x xxxx when read
				'AddRBits(0, 1)              'Add Match Selector Bit
		End Select

		'DO NOT RESET LitCnt HERE!!!

		Exit Sub
Err:
		MsgBox(ErrorToString(), vbOKOnly + vbExclamation, Reflection.MethodBase.GetCurrentMethod.Name + " Error")

	End Sub

	Private Sub AddRBits(Bit As Integer, BCnt As Byte)
		On Error GoTo Err

		For I As Integer = BCnt - 1 To 0 Step -1
			If (Bit And 2 ^ I) <> 0 Then
				Buffer(BitCnt) = Buffer(BitCnt) Or 2 ^ (BitPos - 8)
			End If
			BitPos -= 1
			If BitPos < 8 Then
				BitPos += 8
				BitCnt += 1
			End If
		Next

		Exit Sub
Err:
		MsgBox(ErrorToString(), vbOKOnly + vbExclamation, Reflection.MethodBase.GetCurrentMethod.Name + " Error")

	End Sub

	Public Sub CloseBuffer()  'CHANGE TO PUBLIC
		On Error GoTo Err

		Buffer(ByteCnt) = EndTag
		Buffer(0) = Buffer(0) And &H7F                    'Delete Compression Bit (Default (i.e. compressed) value is 0)

		'FIND UNCOMPRESSIBLE BLOCKS (only applies to first file in buffer in which case LastByte points at the very first data byte in stream)

		If (StartPos - SI <= LastByte) And (StartPos > LastByte - 1) And (NextFileInBuffer = False) Then
			'If (StartPos - SI <= 100) And (StartPos > LastByte - 1) And (NextFileInBuffer = False) Then
			'MsgBox(Hex(StartPos - SI))
			'Less than 252/253 bytes        AND  not the end of File     AND       No other files in this buffer
			LastByte = AdLoPos - 2

			'Check uncompressed Block IO Status
			'If (CheckIO(MatchStart) Or CheckIO(MatchStart - (LastByte - 1)) = 1) And (FileUnderIO = True) Then
			If CheckIO(StartPos - 1) Or CheckIO(StartPos - 1 - (LastByte - 1)) = 1 Then
				'If the block will be UIO than only (Lastbyte-1) bytes will fit,
				'So we only need to check that many bytes
				Buffer(AdLoPos - 1) = 0 'Set IO Flag
				AdHiPos = AdLoPos - 2   'Updae AdHiPos
				LastByte = AdHiPos - 1  'Update LastByte
				'POffset += 1
				'ElseIf (CheckIO(MatchStart - LastByte) = 1) And (FileUnderIO = True) Then
			ElseIf CheckIO(StartPos - 1 - LastByte) = 1 Then
				'If only the last byte is UIO then this byte will be ignored
				'And one less bytes will be stored uncompressed
				AdHiPos = AdLoPos - 1   'IO flag is not set, update AdHiPos
				LastByte = AdHiPos - 2  'But LastByte is decreased by an additional value
				'As the very last byte would go UIO and would need an additional IO Flag byte
			Else
				'Block will not go UIO
				'IO Flag will not be set
				AdHiPos = AdLoPos - 1   'Update AdHiPos
				LastByte = AdHiPos - 1  'And LastByte
			End If

			SI = StartPos - LastByte                         'Update POffset

			Buffer(AdHiPos) = Int((PrgAdd + SI) / 256)  'SI is 1 based
			Buffer(AdLoPos) = (PrgAdd + SI) Mod 256     'SI is 1 based

			For I As Integer = 0 To LastByte - 1            '-1 because the first byte of the buffer is the bitstream
				Buffer(LastByte - I) = Prg(StartPos - I)
			Next

			Buffer(0) = &H80                                        'Set Copression Bit to 1 (=Uncompressed block)
			ByteCnt = 1

		End If

		BlockCnt += 1
		BufferCnt += 1
		UpdateByteStream()

		ResetBuffer()               'Resets buffer variables

		NextFileInBuffer = False            'Reset Next File flag

		If SI < 0 Then Exit Sub 'We have reached the end of the file -> exit

		'If we have not reached the end of the file, then update buffer

		Buffer(ByteCnt) = (PrgAdd + SI) Mod 256
		AdLoPos = ByteCnt

		BlockUnderIO = CheckIO(SI)          'Check if last byte of prg could go under IO

		If BlockUnderIO = 1 Then
			ByteCnt -= 1
		End If

		Buffer(ByteCnt - 1) = Int((PrgAdd + SI) / 256) Mod 256
		AdHiPos = ByteCnt - 1
		ByteCnt -= 2
		LastByte = ByteCnt               'LastByte = the first byte of the ByteStream after and Address Bytes (253 or 252 with blockCnt)

		CalcSequence(IIf(SI > 1, SI, 1), IIf(SI > 1, SI, 1))
		If BlockCnt > 1 Then
			If (Seq(SI).Bit + 8 < LastByte * 8) And (LastFileOfPart = True) Then
				'MsgBox("!")
				'CalcSequence(IIf(SI > 1, SI, 1), IIf(SI - 256 > 1, SI - 256, 1))
			Else
				CalcSequence(IIf(SI > 1, SI, 1), IIf(SI - 256 > 1, SI - 256, 1))
			End If
		End If

		StartPos = SI

		Exit Sub
Err:
		MsgBox(ErrorToString(), vbOKOnly + vbExclamation, Reflection.MethodBase.GetCurrentMethod.Name + " Error")

	End Sub

	Public Function ClosePart(Optional NextFileIO As Integer = 1, Optional LastPartOnDisk As Boolean = False) As Boolean
		On Error GoTo Err

		ClosePart = True

		'ADDS NEW PART TAG (Long Match Tag + End Tag) TO THE END OF THE PART, AND RESERVES LAST BYTE IN BUFFER FOR BLOCK COUNT

		Dim Bytes As Integer = 6 'BYTES NEEDED: BlockCnt + Long Match Tag + End Tag + AdLo + AdHi + 1st Literal (ByC=7 if BlockUnderIO=true - checked at SequenceFits)

		Dim Bits As Integer = IIf((LitCnt = -1) Or (LitCnt Mod (MaxLitLen + 1) = MaxLitLen), 1, 0)   'Calculate whether Match Bit is needed for new part

		If SequenceFits(Bytes, Bits, LitCnt, NextFileIO) Then

			'Buffer has enough space for New Part Tag and New Part Info and first Literal byte (and IO flag if needed)

			If Bits = 1 Then AddRBits(0, 1)
			'If (LitCnt = -1) Or (LitCnt Mod (MaxLitLen + 1) = MaxLitLen) Then AddRBits(0, 1)  'Add Match Selector Bit only if needed
NextPart:
			FilesInBuffer += 1  'There is going to be more than 1 file in the buffer

			If (BufferCnt > 0) And (FilesInBuffer = 2) Then         'Reserve last byte in buffer for Block Count...
				For I = ByteCnt + 1 To 255                          '... only once, when the 2nd file is added to the same buffer
					Buffer(I - 1) = Buffer(I)
				Next
				ByteCnt -= 1
				Buffer(255) = 1                                     'Last byte reserved for BlockCnt
			End If

			Buffer(ByteCnt) = LongMatchTag                          'Then add New File Match Tag
			Buffer(ByteCnt - 1) = EndTag
			ByteCnt -= 2

			If LastPartOnDisk = True Then
				Buffer(ByteCnt) = ByteCnt - 2   'Finish disk with a dummy literal byte that overwrites itself to reset LastX for next disk side
				Buffer(ByteCnt - 1) = &H3
				Buffer(ByteCnt - 2) = &H0
				LitCnt = 0
				AddRBits(0, 1)
				'AddLitBits()                   'NOT NEEDED, WE ARE IN THE MIDDLE OF THE BUFFER, 1ST BIT NEEDS TO BE OMITTED
				Buffer(ByteCnt - 3) = &H0       'ADD 2ND BIT SEPARATELY (0-BIT, TECHNCALLY, THIS IS NOT NEEDED)
				ByteCnt -= 4
			End If

			'DO NOT CLOSE LAST BUFFER HERE, WE ARE GOING TO ADD NEXT PART TO LAST BUFFER

			If ByteSt.Count > BlockPtr Then     'Only save block count if block is already added to ByteSt
				ByteSt(BlockPtr) = LastBlockCnt
				LoaderParts += 1
			End If

			LitCnt = -1                                                 'Reset LitCnt here
		Else
			'Next File Info does not fit, so close buffer
			CloseBuffer()
			'Then add 1 dummy literal byte to new block (blocks must start with 1 literal, next part tag is a match tag)
			Buffer(255) = &HFC          'Dummy Address ($03fc* - first literal's address in buffer... (*NextPart above, will reserve BlockCnt)
			Buffer(254) = &H3           '...we are overwriting it with the same value
			Buffer(253) = &H0           'Dummy value, will be overwritten with itself
			LitCnt = 0
			AddLitBits()                'WE NEED THIS HERE, AS THIS IS THE BEGINNING OF THE BUFFER, AND 1ST BIT WILL BE CHANGED TO COMPRESSION BIT
			ByteCnt = 252
			LastBlockCnt += 1

			If LastBlockCnt > 255 Then
				'Parts cannot be larger than 255 blocks compressed
				'There is some confusion here how PartCnt is used in the Editor and during Disk building...
				MsgBox("Part " + IIf(CompressPartFromEditor = True, PartCnt + 1, PartCnt).ToString + " would need " + LastBlockCnt.ToString + " blocks on the disk." + vbNewLine + vbNewLine + "Parts cannot be larger than 255 blocks!", vbOKOnly + vbCritical, "Part exceeds 255-block limit!")
				If CompressPartFromEditor = False Then GoTo NoGo
			End If

			BlockCnt -= 1
			'THEN GOTO NEXT PART SECTION
			GoTo NextPart
		End If

		Exit Function
Err:
		MsgBox(ErrorToString(), vbOKOnly + vbExclamation, Reflection.MethodBase.GetCurrentMethod.Name + " Error")

NoGo:
		ClosePart = False

	End Function

	Public Sub CloseFile()
		On Error GoTo Err

		'ADDS NEXT FILE TAG TO BUFFER

		'4 bytes and 0-1 bits needed for NextFileTag, Address Bytes and first Lit byte (+1 more if UIO)
		Dim Bytes As Integer = 4 'BYTES NEEDED: End Tag + AdLo + AdHi + 1st Literal (ByC=5 only if BlockUnderIO=true - checked at SequenceFits()
		Dim Bits As Integer = IIf((LitCnt = -1) Or (LitCnt Mod (MaxLitLen + 1) = MaxLitLen), 1, 0)   'Calculate whether Match Bit is needed for new file

		If SequenceFits(Bytes, Bits, LitCnt, CheckIO(PrgLen - 1)) Then

			'Buffer has enough space for New File Match Tag and New File Info and first Literal byte (and IO flag if needed)

			If Bits = 1 Then AddRBits(0, 1)
			'If (LitCnt = -1) Or (LitCnt Mod (MaxLitLen + 1) = MaxLitLen) Then AddRBits(0, 1)  'Add Match Selector Bit only if needed

			Buffer(ByteCnt) = NextFileTag                           'Then add New File Match Tag
			ByteCnt -= 1
		Else
			'Next File Info does not fit, so close buffer
			CloseBuffer()
		End If

		Exit Sub
Err:
		MsgBox(ErrorToString(), vbOKOnly + vbExclamation, Reflection.MethodBase.GetCurrentMethod.Name + " Error")

	End Sub

End Module
