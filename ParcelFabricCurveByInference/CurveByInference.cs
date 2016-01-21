﻿using ESRI.ArcGIS.CadastralUI;
using ESRI.ArcGIS.Carto;
using ESRI.ArcGIS.Display;
using ESRI.ArcGIS.Editor;
using ESRI.ArcGIS.esriSystem;
using ESRI.ArcGIS.Framework;
using ESRI.ArcGIS.Geodatabase;
using ESRI.ArcGIS.GeoDatabaseExtensions;
using ESRI.ArcGIS.Geometry;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows.Forms;

namespace ParcelFabricCurveByInference
{
    public class InferredCurve : INotifyPropertyChanged
    {
        public int ObjectID { get; set; }
        public string Description { get; set; }
        public string LayerName { get { return Layer.Name; } }
        public IFeatureLayer Layer { get; set; }

        public RelatedCurve _Accepted;
        public RelatedCurve Accepted { get { return _Accepted; } set { _Accepted = value; RaisePropertyChanged("Accepted", "SetVisibility", "ItemColor"); } }

        public System.Windows.Media.Brush ItemColor { get { return (Accepted == null) ? System.Windows.Media.Brushes.Black : System.Windows.Media.Brushes.Blue; } }

        public List<RelatedCurve> _TangentCurves;
        public List<RelatedCurve> TangentCurves { get { return _TangentCurves; } set { _TangentCurves = value; RaisePropertyChanged("TangentCurves", "TangentVisibility"); } }
        public System.Windows.Visibility TangentVisibility { get { return (TangentCurves == null || TangentCurves.Count == 0) ? System.Windows.Visibility.Collapsed : System.Windows.Visibility.Visible; } }

        public List<RelatedCurve> _ParallelCurves;
        public List<RelatedCurve> ParallelCurves { get { return _ParallelCurves; } set { _ParallelCurves = value; RaisePropertyChanged("ParallelCurves", "ParallelVisibility"); } }
        public System.Windows.Visibility ParallelVisibility { get { return (ParallelCurves == null || ParallelCurves.Count == 0) ? System.Windows.Visibility.Collapsed : System.Windows.Visibility.Visible; } }

        public string Header { get; set; }
        public System.Windows.Visibility SetVisibility { get { return (Accepted == null) ? System.Windows.Visibility.Collapsed : System.Windows.Visibility.Visible; } }

        public InferredCurve(int id, IFeatureLayer layer, string description)
        {
            this.ObjectID = id;
            this.Description = description;
            this.Layer = layer;

            this.Header = string.Format("{0} - {1}", LayerName, ObjectID);

            TangentCurves = new List<RelatedCurve>();
            ParallelCurves = new List<RelatedCurve>();
        }

        public event PropertyChangedEventHandler PropertyChanged;
        private void RaisePropertyChanged(params string[] properties)
        {
            try
            {
                //if (UIDispatcher != null && !UIDispatcher.CheckAccess())
                //    UIDispatcher.Invoke(() => { RaisePropertyChanged(properties); });


                if (PropertyChanged != null)
                    foreach (string property in properties)
                        PropertyChanged(this, new PropertyChangedEventArgs(property));
            }
            catch (Exception exx)
            {
                System.Diagnostics.Debug.WriteLine(exx.Message);
            }
        }
    }

    public enum RelationTypes { Tangent, Parallel }
    public class RelatedCurve
    {
        public int ObjectID { get; set; }

        public double Radius { get; set; }
        public int CenterpointID { get; set; }

        public CurveByInference.RelativeOrientation Orientation { get; set; }

        public RelationTypes RelationType { get; set; }

        public string Header { get; set; }

        public RelatedCurve(int id, double radius, int centerpointID, RelationTypes relationType)
        {
            this.ObjectID = id;
            this.Radius = radius;
            this.CenterpointID = centerpointID;
            this.RelationType = relationType;

            this.Header = String.Format("{0}: CPID:{1} rad:{2}", id, centerpointID, radius);
        }
    }
    public class RelatedCurveComparer : IEqualityComparer<RelatedCurve>
    {
        public bool Equals(RelatedCurve x, RelatedCurve y)
        {
            return (x.CenterpointID == y.CenterpointID && Math.Abs(x.Radius - y.Radius) < 0.005);
        }

        public int GetHashCode(RelatedCurve obj)
        {
            return (int)(obj.CenterpointID * obj.Radius);
        }
    }

       


    public class CurveByInference : INotifyPropertyChanged
    {
        public ObservableCollection<InferredCurve> Curves { get; private set; }

        bool _Finished;
        public bool Finished { get { return _Finished; } set { _Finished = value; RaisePropertyChanged("Finished"); } }

        CancelTracker cancelTracker = new CancelTracker();
        ProgressDialogFactory progressDialogFactory = new ProgressDialogFactory();
        IStepProgressor stepProgress;

        public CurveByInference()
        {
            Finished = true;
            Curves = new ObservableCollection<InferredCurve>();
        }


        public void FindCurves(bool UpdateCurves)
        {
            Finished = false;

            setStepProgressorProperties(1, "Initializing");
            IMouseCursor pMouseCursor = new MouseCursorClass();
            pMouseCursor.SetCursor(2);

            try
            {
                IMap pMap = null;

                IEditor m_pEd = (IEditor)ArcMap.Application.FindExtensionByName("esri object editor");
                if (UpdateCurves)
                {
                    if (m_pEd.EditState == esriEditState.esriStateNotEditing)
                    {
                        ShowError("Please start editing first, and try again.");
                        return;
                    }
                    pMap = m_pEd.Map;
                }
                else
                {
                    pMap = ArcMap.Document.ActiveView.FocusMap;
                }

                //get Cadastral Editor
                UID pUID = new UIDClass();
                pUID.Value = "{114D685F-99B7-4B63-B09F-6D1A41A4DDC1}";
                ICadastralExtensionManager2 pCadExtMan = (ICadastralExtensionManager2)ArcMap.Application.FindExtensionByCLSID(pUID);
                ICadastralEditor pCadEd = (ICadastralEditor)ArcMap.Application.FindExtensionByCLSID(pUID);

                //check if there is a Manual Mode "modify" job active ===========
                ICadastralPacketManager pCadPacMan = (ICadastralPacketManager)pCadExtMan;
                if (pCadPacMan.PacketOpen)
                {
                    ShowError("The command cannot be used when there is an open job.\r\nPlease finish or discard the open job, and try again.");
                    return;
                }

                try
                {
                    IEditProperties2 pEditorProps2 = (IEditProperties2)m_pEd;

                    IArray LineLyrArr;
                    ICadastralFabric pCadFabric = null;
                    IActiveView pActiveView = ArcMap.Document.ActiveView;

                    IAngularConverter pAngConv = new AngularConverterClass();

                    if (!GetFabricSubLayers(pMap, esriCadastralFabricTable.esriCFTLines, out LineLyrArr))
                    {
                        ShowError("No cadastral fabric line feature classes found in the current map.");
                        return;
                    }

                    //if we're in an edit session then grab the target fabric
                    if (m_pEd.EditState == esriEditState.esriStateEditing)
                        pCadFabric = pCadEd.CadastralFabric;

                    //find the first fabric in the map
                    if (pCadFabric == null)
                    {
                        if (!GetFabricFromMap(pMap, out pCadFabric))
                        {
                            ShowError("No Parcel Fabric found in the map.\r\nPlease add a single fabric to the map, and try again.");
                            return;
                        }
                    }

                    //Verify that the needed fields are present                                
                    IFeatureClass pFabricLinesFC = (IFeatureClass)pCadFabric.get_CadastralTable(esriCadastralFabricTable.esriCFTLines);
                    int idxParcelIDFld = pFabricLinesFC.Fields.FindField("ParcelID");
                    int idxCENTERPTID = pFabricLinesFC.Fields.FindField("CenterPointID");
                    int idxRADIUS = pFabricLinesFC.Fields.FindField("Radius");
                    if (idxParcelIDFld == -1 || idxCENTERPTID == -1 || idxRADIUS == -1)
                    {
                        ShowError("One or more of the following fields are missing (ParcelID, CenterPointID, Radius).");
                        return;
                    }

                    HashSet<int> parcelsToLock = new HashSet<int>();

                    //for each CF line feature class 
                    for (int i = 0; i < LineLyrArr.Count; i++)
                    {
                        IFeatureLayer layer = LineLyrArr.Element[i] as IFeatureLayer;

                        //ISelectionSet pSelSet = layer.FeatureClass.Select(null, esriSelectionType.esriSelectionTypeIDSet, esriSelectionOption.esriSelectionOptionNormal, null);

                        setStepProgressorProperties(((ITable)layer.FeatureClass).RowCount(null), String.Format("Layer {0}: Evaluating Features", layer.Name));
                        IFeatureCursor cursor = layer.FeatureClass.Search(null, true);
                        IFeature pLineFeat = null;
                        while ((pLineFeat = cursor.NextFeature() as IFeature) != null)
                        {
                            if (!cancelTracker.Continue())
                                break;
                            stepProgress.Step();

                            if (!Curves.Any(w => w.ObjectID == pLineFeat.OID))
                            {
                                IGeometry pGeom = pLineFeat.ShapeCopy;
                                ISegmentCollection pSegColl = pGeom as ISegmentCollection;
                                ISegment pSeg = null;
                                if (pSegColl.SegmentCount == 1)
                                    pSeg = pSegColl.get_Segment(0);
                                else
                                {
                                    //todo: but for now, only deals with single segment short segments
                                    Marshal.ReleaseComObject(pLineFeat);
                                    continue;
                                }

                                //if the geometry is a circular arc and the attributes reflect that, move on to the next feature
                                if (pSeg is ICircularArc)
                                {
                                    object dVal1 = pLineFeat.get_Value(idxRADIUS);
                                    object dVal2 = pLineFeat.get_Value(idxCENTERPTID);
                                    if (!(dVal1 is DBNull) && !(dVal2 is DBNull))
                                    {
                                        Marshal.ReleaseComObject(pLineFeat);
                                        continue;
                                    }
                                }

                                //query near lines
                                List<RelatedCurve> sCurveInfoFromNeighbours = GetTangentCurveMatchFeatures(pFabricLinesFC, (IPolycurve)pGeom, "", pSeg.Length);
                                
                                if(sCurveInfoFromNeighbours.Count > 0)
                                //if (HasTangentCurveMatchFeatures(pFabricLinesFC, (IPolycurve)pGeom, "", pSeg.Length, out iFoundTangent, ref sCurveInfoFromNeighbours))
                                {
                                    //lstLineIds.Add(pLineFeat.OID);
                                    InferredCurve curve = new InferredCurve(pLineFeat.OID, layer, "Inferred Curve By Tangent");
                                    Curves.Add(curve);
                                    curve.TangentCurves = sCurveInfoFromNeighbours;
 
                                    //if there's only one tangent look further afield
                                    if (curve.TangentCurves.Count == 1)
                                        curve.ParallelCurves = GetParallelCurveMatchFeatures(pFabricLinesFC, (IPolycurve)pGeom, "");

                                    //Determin if there is assume information to actually set the radius and centerpoint
                                    RefineToBestRadiusAndCenterPoint(curve);

                                    //if the curve has an accepted curve (ie, it's a candidate to be changed), record the parcel id
                                    if (curve.Accepted != null)
                                        parcelsToLock.Add((int)pLineFeat.get_Value(idxParcelIDFld));
                                }
                            }
                            Marshal.ReleaseComObject(pLineFeat);
                        }
                        Marshal.ReleaseComObject(cursor);
                    }

                    if (Curves.Count == 0)
                    {
                        ShowError("No inferred curved lines found.");
                        return;
                    }

                    //RefineToBestRadiusAndCenterPoint(inferredCurvesLookup, dictLineToCurveNeighbourData);

                    //Test to see if there are actually any updates to apply.  
                    if (!UpdateCurves || Curves.Count == 0 || parcelsToLock.Count == 0)
                        return;

                    bool bIsFileBasedGDB = false;
                    bool bIsUnVersioned = false;
                    bool bUseNonVersionedDelete = false;
                    IWorkspace pWS = m_pEd.EditWorkspace;

                    if (!SetupEditEnvironment(pWS, pCadFabric, m_pEd, out bIsFileBasedGDB, out bIsUnVersioned, out bUseNonVersionedDelete))
                    {
                        ShowError("The editing environment could not be initialized");
                        return;
                    }


                    #region Create Cadastral Job
                    string sTime = "";
                    if (!bIsUnVersioned && !bIsFileBasedGDB)
                    {
                        //see if parcel locks can be obtained on the selected parcels. First create a job.
                        DateTime localNow = DateTime.Now;
                        sTime = Convert.ToString(localNow);
                        ICadastralJob pJob = new CadastralJob();
                        pJob.Name = sTime;
                        pJob.Owner = System.Windows.Forms.SystemInformation.UserName;
                        pJob.Description = "Convert lines to curves";
                        try
                        {
                            Int32 jobId = pCadFabric.CreateJob(pJob);
                        }
                        catch (COMException ex)
                        {
                            if (ex.ErrorCode == (int)fdoError.FDO_E_CADASTRAL_FABRIC_JOB_ALREADY_EXISTS)
                            {
                                ShowError("Job named: '" + pJob.Name + "', already exists");
                            }
                            else
                            {
                                ShowError(ex.Message);
                            }
                            return;
                        }
                    }
                    #endregion

                    #region Test for Edit Locks
                    ICadastralFabricLocks pFabLocks = (ICadastralFabricLocks)pCadFabric;

                    //only need to get locks for parcels that have lines that are to be changed


                    ILongArray pParcelsToLock = new LongArrayClass();
                    foreach (int i in parcelsToLock)
                        pParcelsToLock.Add(i);

                    if (!bIsUnVersioned && !bIsFileBasedGDB)
                    {
                        pFabLocks.LockingJob = sTime;
                        ILongArray pLocksInConflict = null;
                        ILongArray pSoftLcksInConflict = null;

                        if (!bIsFileBasedGDB)
                            stepProgress.Message = "Testing for edit locks on parcels...";

                        try
                        {
                            pFabLocks.AcquireLocks(pParcelsToLock, true, ref pLocksInConflict, ref pSoftLcksInConflict);
                        }
                        catch (COMException pCOMEx)
                        {
                            if (pCOMEx.ErrorCode == (int)fdoError.FDO_E_CADASTRAL_FABRIC_JOB_LOCK_ALREADY_EXISTS ||
                              pCOMEx.ErrorCode == (int)fdoError.FDO_E_CADASTRAL_FABRIC_JOB_CURRENTLY_EDITED)
                            {
                                ShowError("Edit Locks could not be acquired on all selected parcels.");
                                // since the operation is being aborted, release any locks that were acquired
                                pFabLocks.UndoLastAcquiredLocks();
                            }
                            else
                                ShowError(pCOMEx.Message + Environment.NewLine + Convert.ToString(pCOMEx.ErrorCode));

                            return;
                        }
                    }
                    #endregion

                    if (m_pEd.EditState == esriEditState.esriStateEditing)
                    {
                        try
                        {
                            m_pEd.StartOperation();
                        }
                        catch
                        {
                            m_pEd.AbortOperation();//abort any open edit operations and try again
                            m_pEd.StartOperation();
                        }
                    }
                    if (bUseNonVersionedDelete)
                    {
                        if (!StartEditing(pWS, bIsUnVersioned))
                        {
                            ShowError("Couldn't start an edit session");
                            return;
                        }
                    }

                    ICadastralFabricSchemaEdit2 pSchemaEd = (ICadastralFabricSchemaEdit2)pCadFabric;
                    pSchemaEd.ReleaseReadOnlyFields((ITable)pFabricLinesFC, esriCadastralFabricTable.esriCFTLines); //release for edits

                    IQueryFilter m_pQF = new QueryFilter();

                    // m_pEd.StartOperation();

                    //copy all the curves that have an accepted curve to a dictionay
                    var ToUpdate = (from c in Curves where c.Accepted != null select c).ToList();

                    //Slice the list into sets that will fit into an in list
                    setStepProgressorProperties(ToUpdate.Count, "Updating geometries");
                    for (var i = 0; i < ToUpdate.Count; i += 995)
                    {
                        Dictionary<int, InferredCurve> curvesSlice = ToUpdate.Skip(i).Take(995).ToDictionary(w => w.ObjectID);
                        if (!cancelTracker.Continue())
                            return;
                        m_pQF.WhereClause = String.Format("{0} IN ({1})", pFabricLinesFC.OIDFieldName, String.Join(",", (from oid in curvesSlice.Keys select oid.ToString()).ToArray()));
                        UpdateCircularArcValues((ITable)pFabricLinesFC, m_pQF, bIsUnVersioned, curvesSlice, cancelTracker);
                    }

                    //List<string> sInClauseList = InClauseFromOIDsList(Curves, 995);
                    //foreach (string InClause in sInClauseList)
                    //{
                    //    if (!cancelTracker.Continue())
                    //        return;

                    //    m_pQF.WhereClause = pFabricLinesFC.OIDFieldName + " IN (" + InClause + ")";
                    //    if (!UpdateCircularArcValues((ITable)pFabricLinesFC, m_pQF, bIsUnVersioned, Curves, stepProgress, cancelTracker))
                    //        ;
                    //}
                    m_pEd.StopOperation("Insert missing circular arc information.");

                }
                catch (Exception ex)
                {
                    ShowError(ex.Message);
                    if (m_pEd != null && m_pEd.HasEdits())
                    {
                        m_pEd.AbortOperation();
                    }
                }
                finally
                {
                }
            }
            finally
            {

                if (pMouseCursor != null)
                    Marshal.ReleaseComObject(pMouseCursor);
                if (stepProgress != null)
                {
                    //Not sure if this should be disposed as a IProgressDialog2 or IStepProgressor.  I don't know if it matters
                    //IProgressDialog2::HideDialog needs to be called or the dialog hangs around
                    //IStepProgressor::Hide isn't enough
                    ((IProgressDialog2)stepProgress).HideDialog();
                    Marshal.ReleaseComObject((IProgressDialog2)stepProgress);
                }
                Finished = true;
            }
        }

        #region Access

        bool GetFabricFromMap(IMap InMap, out ICadastralFabric Fabric)
        {//this code assumes only one fabric in the map, and will get the first that it finds.
            //Used when not in an edit session. TODO: THis could return an array of fabrics
            Fabric = null;
            for (int idx = 0; idx <= (InMap.LayerCount - 1); idx++)
            {
                ILayer pLayer = InMap.get_Layer(idx);
                if (GetFabricFromLayer(pLayer, out Fabric))
                    return true;
            }
            return false;
        }
        private bool GetFabricFromLayer(ILayer Layer, out ICadastralFabric Fabric)
        {
            //interrogates a layer and returns it's source fabric if it is a fabric layer

            ICompositeLayer pCompLyr = Layer as ICompositeLayer;
            if (pCompLyr != null)
            {
                for (int i = 0; i <= pCompLyr.Count; i++)
                    if (GetFabricFromLayer(pCompLyr.get_Layer(i), out Fabric))
                        return true;
            }

            ICadastralFabricLayer pCFLayer = Layer as ICadastralFabricLayer;
            if (pCFLayer != null)
            {
                Fabric = pCFLayer.CadastralFabric;
                return true;
            }

            ICadastralFabricSubLayer pCFSubLyr = Layer as ICadastralFabricSubLayer;
            if (pCFSubLyr != null)
            {
                Fabric = pCFSubLyr.CadastralFabric;
                return true;
            }

            Fabric = null;
            return false;
        }

        bool GetFabricSubLayers(IMap Map, esriCadastralFabricTable FabricSubClass, out IArray CFParcelFabSubLayers)
        {
            ICadastralFabricSubLayer pCFSubLyr = null;
            IArray CFParcelFabricSubLayers2 = new ArrayClass();
            IFeatureLayer pParcelFabricSubLayer = null;
            UID pId = new UIDClass();
            pId.Value = "{E156D7E5-22AF-11D3-9F99-00C04F6BC78E}";
            IEnumLayer pEnumLayer = Map.get_Layers(pId, true);
            pEnumLayer.Reset();
            ILayer pLayer = pEnumLayer.Next();
            while (pLayer != null)
            {
                if (pLayer is ICadastralFabricSubLayer)
                {
                    pCFSubLyr = (ICadastralFabricSubLayer)pLayer;
                    if (pCFSubLyr.CadastralTableType == FabricSubClass)
                    {
                        pParcelFabricSubLayer = (IFeatureLayer)pCFSubLyr;
                        CFParcelFabricSubLayers2.Add(pParcelFabricSubLayer);
                    }
                }
                pLayer = pEnumLayer.Next();
            }
            CFParcelFabSubLayers = CFParcelFabricSubLayers2;
            if (CFParcelFabricSubLayers2.Count > 0)
                return true;
            else
                return false;
        }

        #endregion

        #region feature updating

        public bool SetupEditEnvironment(IWorkspace TheWorkspace, ICadastralFabric TheFabric, IEditor TheEditor, out bool IsFileBasedGDB, out bool IsUnVersioned, out bool UseNonVersionedEdit)
        {
            IsFileBasedGDB = false;
            IsUnVersioned = false;
            UseNonVersionedEdit = false;

            ITable pTable = TheFabric.get_CadastralTable(esriCadastralFabricTable.esriCFTParcels);

            IsFileBasedGDB = (!(TheWorkspace.WorkspaceFactory.WorkspaceType == esriWorkspaceType.esriRemoteDatabaseWorkspace));

            if (!(IsFileBasedGDB))
            {
                IVersionedObject pVersObj = (IVersionedObject)pTable;
                IsUnVersioned = (!(pVersObj.IsRegisteredAsVersioned));
                pTable = null;
                pVersObj = null;
            }
            if (IsUnVersioned && !IsFileBasedGDB)
            {//
                DialogResult dlgRes = MessageBox.Show("Fabric is not registered as versioned." +
                  "\r\n You will not be able to undo." +
                  "\r\n Click 'OK' to delete permanently.",
                  "Continue with delete?", MessageBoxButtons.OKCancel);
                if (dlgRes == DialogResult.OK)
                {
                    UseNonVersionedEdit = true;
                }
                else if (dlgRes == DialogResult.Cancel)
                {
                    return false;
                }
                //MessageBox.Show("The fabric tables are non-versioned." +
                //   "\r\n Please register as versioned, and try again.");
                //return false;
            }
            else if ((TheEditor.EditState == esriEditState.esriStateNotEditing))
            {
                MessageBox.Show("Please start editing first and try again.", "Delete",
                  MessageBoxButtons.OK, MessageBoxIcon.Information);
                return false;
            }
            return true;
        }

        public void FIDsetToLongArray(IFIDSet InFIDSet, ref ILongArray OutLongArray, ref int[] OutIntArray)
        {
            setStepProgressorProperties(InFIDSet.Count(), "Converting ObjectIDs");

            Int32 pfID = -1;
            InFIDSet.Reset();
            double dMax = InFIDSet.Count();
            int iMax = (int)(dMax);
            for (Int32 pCnt = 0; pCnt <= (InFIDSet.Count() - 1); pCnt++)
            {
                InFIDSet.Next(out pfID);
                OutLongArray.Add(pfID);
                OutIntArray[pCnt] = pfID;
                if (stepProgress != null)
                {
                    if (stepProgress.Position < stepProgress.MaxRange)
                        stepProgress.Step();
                }
            }
            return;
        }

        public bool StartEditing(IWorkspace TheWorkspace, bool IsUnversioned)   // Start EditSession + create EditOperation
        {
            bool IsFileBasedGDB =
              (!(TheWorkspace.WorkspaceFactory.WorkspaceType == esriWorkspaceType.esriRemoteDatabaseWorkspace));

            IWorkspaceEdit pWSEdit = (IWorkspaceEdit)TheWorkspace;
            if (pWSEdit.IsBeingEdited())
            {
                ShowError("The workspace is being edited by another process.");
                return false;
            }

            if (!IsFileBasedGDB)
            {
                IMultiuserWorkspaceEdit pMUWSEdit = (IMultiuserWorkspaceEdit)TheWorkspace;
                try
                {
                    if (pMUWSEdit.SupportsMultiuserEditSessionMode(esriMultiuserEditSessionMode.esriMESMNonVersioned) && IsUnversioned)
                    {
                        pMUWSEdit.StartMultiuserEditing(esriMultiuserEditSessionMode.esriMESMNonVersioned);
                    }
                    else if (pMUWSEdit.SupportsMultiuserEditSessionMode(esriMultiuserEditSessionMode.esriMESMVersioned) && !IsUnversioned)
                    {
                        pMUWSEdit.StartMultiuserEditing(esriMultiuserEditSessionMode.esriMESMVersioned);
                    }

                    else
                    {
                        return false;
                    }
                }
                catch (COMException ex)
                {
                    MessageBox.Show(ex.Message + "  " + Convert.ToString(ex.ErrorCode), "Start Editing");
                    return false;
                }
            }
            else
            {
                try
                {
                    pWSEdit.StartEditing(false);
                }
                catch (COMException ex)
                {
                    MessageBox.Show(ex.Message + "  " + Convert.ToString(ex.ErrorCode), "Start Editing");
                    return false;
                }
            }

            pWSEdit.DisableUndoRedo();
            try
            {
                pWSEdit.StartEditOperation();
            }
            catch
            {
                pWSEdit.StopEditing(false);
                return false;
            }
            return true;
        }

        public List<string> InClauseFromOIDsList(IList<InferredCurve> ListOfOids, int TokenMax)
        {
            List<string> InClause = new List<string>();
            int iCnt = 0;
            int iIdx = 0;
            InClause.Add("");
            foreach (InferredCurve i in ListOfOids)
            {
                if (iCnt == TokenMax)
                {
                    InClause.Add("");
                    iCnt = 0;
                    iIdx++;
                }
                if (InClause[iIdx].Trim() == "")
                    InClause[iIdx] = i.ObjectID.ToString();
                else
                    InClause[iIdx] += "," + i.ObjectID.ToString();
                iCnt++;
            }
            return InClause;
        }

        public bool UpdateCircularArcValues(ITable LineTable, IQueryFilter QueryFilter, bool Unversioned, IDictionary<int, InferredCurve> CurveLookup, CancelTracker cancelTracker)
        {
            try
            {
                ITableWrite pTableWr = (ITableWrite)LineTable;//used for unversioned table
                IRow pLineFeat = null;
                ICursor pLineCurs = null;

                if (Unversioned)
                    pLineCurs = pTableWr.UpdateRows(QueryFilter, false);
                else
                    pLineCurs = LineTable.Update(QueryFilter, false);

                pLineFeat = null;

                Int32 iCtrPointIDX = pLineCurs.Fields.FindField("CENTERPOINTID");
                Int32 iRadiusIDX = pLineCurs.Fields.FindField("RADIUS");

                while ((pLineFeat = pLineCurs.NextRow()) != null)
                {//loop through all of the given lines, and update centerpoint ids and the Radius values
                    if (!cancelTracker.Continue())
                        return false;
                    stepProgress.Step();

                    //List<string> CurveInfoList = CurveLookup[pLineFeat.OID];
                    //InferredCurve CurveInfoList = CurveLookup[pLineFeat.OID];

                    //string[] sCurveInfo = CurveInfoList[0].Split(','); //should only be one element in the list at this point
                    //RelatedCurve curveInfo = CurveInfoList.FirstOrDefault(w=>w.Accepted);
                    RelatedCurve curveInfo = CurveLookup[pLineFeat.OID].Accepted;
                    //if (sCurveInfo.Length > 2)
                    //if (!curveInfo.)
                    //{
                    //    Marshal.ReleaseComObject(pLineFeat); //garbage collection
                    //    continue;
                    //}
                    //double dRadius = Convert.ToDouble(sCurveInfo[0]);
                    //int iCtrPointID = Convert.ToInt32(sCurveInfo[1]);
                    pLineFeat.set_Value(iRadiusIDX, curveInfo.Radius);
                    pLineFeat.set_Value(iCtrPointIDX, curveInfo.CenterpointID);

                    if (Unversioned)
                        pLineCurs.UpdateRow(pLineFeat);
                    else
                        pLineFeat.Store();

                    Marshal.ReleaseComObject(pLineFeat); //garbage collection
                }
                Marshal.ReleaseComObject(pLineCurs); //garbage collection
                return true;
            }
            catch (COMException ex)
            {
                MessageBox.Show("Problem updating circular arc: " + Convert.ToString(ex.ErrorCode));
                return false;
            }
        }

        public bool UpdateCircularArcValues(ITable LineTable, IQueryFilter QueryFilter, bool Unversioned, Dictionary<int, List<RelatedCurve>> CurveLookup, CancelTracker cancelTracker)
        {
            try
            {
                ITableWrite pTableWr = (ITableWrite)LineTable;//used for unversioned table
                IRow pLineFeat = null;
                ICursor pLineCurs = null;

                if (Unversioned)
                    pLineCurs = pTableWr.UpdateRows(QueryFilter, false);
                else
                    pLineCurs = LineTable.Update(QueryFilter, false);

                pLineFeat = null;

                Int32 iCtrPointIDX = pLineCurs.Fields.FindField("CENTERPOINTID");
                Int32 iRadiusIDX = pLineCurs.Fields.FindField("RADIUS");

                while ((pLineFeat = pLineCurs.NextRow()) != null)
                {//loop through all of the given lines, and update centerpoint ids and the Radius values
                    if (!cancelTracker.Continue())
                        return false;
                    stepProgress.Step();

                    //List<string> CurveInfoList = CurveLookup[pLineFeat.OID];
                    List<RelatedCurve> CurveInfoList = CurveLookup[pLineFeat.OID];

                    //string[] sCurveInfo = CurveInfoList[0].Split(','); //should only be one element in the list at this point
                    //RelatedCurve curveInfo = CurveInfoList.FirstOrDefault(w=>w.Accepted);
                    RelatedCurve curveInfo = CurveInfoList[0];
                    //if (sCurveInfo.Length > 2)
                    //if (!curveInfo.)
                    //{
                    //    Marshal.ReleaseComObject(pLineFeat); //garbage collection
                    //    continue;
                    //}
                    //double dRadius = Convert.ToDouble(sCurveInfo[0]);
                    //int iCtrPointID = Convert.ToInt32(sCurveInfo[1]);
                    pLineFeat.set_Value(iRadiusIDX, curveInfo.Radius);
                    pLineFeat.set_Value(iCtrPointIDX, curveInfo.CenterpointID);

                    if (Unversioned)
                        pLineCurs.UpdateRow(pLineFeat);
                    else
                        pLineFeat.Store();

                    Marshal.ReleaseComObject(pLineFeat); //garbage collection
                }
                Marshal.ReleaseComObject(pLineCurs); //garbage collection
                return true;
            }
            catch (COMException ex)
            {
                MessageBox.Show("Problem updating circular arc: " + Convert.ToString(ex.ErrorCode));
                return false;
            }
        }

        #endregion

        #region Inference

        RelatedCurveComparer relatedCurveComparer = new RelatedCurveComparer();
        void RefineToBestRadiusAndCenterPoint(InferredCurve inferredCurve)
        {
            if (inferredCurve.TangentCurves.Count == 1 && inferredCurve.ParallelCurves.Count > 0)
            {//only one radius found, so search the parallel offsets for one conformer
                bool bHasConfirmer = false;
                foreach (RelatedCurve dd in inferredCurve.ParallelCurves)
                {
                    if (Math.Abs(dd.Radius - inferredCurve.TangentCurves[0].Radius) < 0.5)
                    {
                        bHasConfirmer = true;
                        break;
                    }
                }
                if (bHasConfirmer)
                {
                    inferredCurve.Accepted = inferredCurve.TangentCurves[0];
                }
                return;
            }

            var groupsTangent = inferredCurve.TangentCurves.GroupBy(item => Math.Round(item.Radius, 2)).Where(group => group.Skip(1).Any());
            var groupsTangentAndCP = inferredCurve.TangentCurves.GroupBy(item => item, relatedCurveComparer).Where(group => group.Skip(1).Any());

            System.Diagnostics.Debug.Print(groupsTangent.Count().ToString());
            System.Diagnostics.Debug.Print(groupsTangentAndCP.Count().ToString());

            if (groupsTangent.Count() == 1 && groupsTangentAndCP.Count() == 1)
            { //if there is only 1 of each group, then there are no ambiguities for the tangent or the center point
                IGrouping<RelatedCurve, RelatedCurve> d1 = groupsTangentAndCP.ElementAt(0);
                inferredCurve.Accepted = d1.Key;
                return;
            }
            else if (groupsTangent.Count() == 1 && groupsTangentAndCP.Count() > 1)
            {//if there is only 1 tangent, but more than one center point then there are center points to merge
                
                //TODO: Add center point Merging here
            }
            if (groupsTangent.Count() > 1)
            { //if there is more than 1 tangent, then ...code stub if needed
                foreach (var value in groupsTangentAndCP)
                {
                    System.Diagnostics.Debug.Print(value.Key.ToString());
                }
            }
        }

        public List<RelatedCurve> GetParallelCurveMatchFeatures(IFeatureClass FeatureClass, IPolycurve inPolycurve, string WhereClause)
            //double AngleToleranceTangentCompareInDegrees, double OrthogonalSearchDistance,
            //                                        out int outFoundLinesCount, out int outFoundParallelCurvesCount, ref List<RelatedCurve> CurveInfoFromNeighbours)
        {
            List<RelatedCurve> CurveInfoFromNeighbours = new List<RelatedCurve>();

            ILine inGeomChord = (ILine)new Line();
            inGeomChord.PutCoords(inPolycurve.FromPoint, inPolycurve.ToPoint);
            IVector3D inGeomVector = (IVector3D)new Vector3D();
            inGeomVector.PolarSet(inGeomChord.Angle, 0, 1);

            int idxRadius = FeatureClass.FindField("RADIUS");
            if (idxRadius == -1)
                return CurveInfoFromNeighbours;

            int idxCenterPointID = FeatureClass.FindField("CENTERPOINTID");
            if (idxCenterPointID == -1)
                return CurveInfoFromNeighbours;

            //generate line segments that are perpendicular to the in feature at half way

            IGeometryBag queryGeomBag = (IGeometryBag)new GeometryBag();
            IGeometryCollection queryGeomPartCollection = (IGeometryCollection)queryGeomBag;

            IGeometry queryMultiPartPolyLine = (IGeometry)new Polyline(); //qi
            IGeoDataset pGeoDS = (IGeoDataset)FeatureClass;
            ISpatialReference spatialRef = pGeoDS.SpatialReference;
            queryMultiPartPolyLine.SpatialReference = spatialRef;

            IGeometryCollection queryGeomCollection = queryMultiPartPolyLine as IGeometryCollection;

            ILine pNormalLine = (ILine)new Line(); //new
            for (int i = -1; i < 2; i = i + 2)
            {
                double dOffset = CurveByInferenceSettings.Instance.OrthogonalSearchDistance * i;

                inPolycurve.QueryNormal(esriSegmentExtension.esriNoExtension, 0.5, true, dOffset, pNormalLine);
                ILine pThisLine = (ILine)new Line();

                pThisLine.PutCoords(pNormalLine.FromPoint, pNormalLine.ToPoint);
                queryGeomPartCollection.AddGeometry(pThisLine);

                //Although each line is connected to the other, create a new path for each line 
                //this allows for future changes in case the input needs to be altered to separate paths.

                ISegmentCollection newPath = (ISegmentCollection)new Path();
                object obj = Type.Missing;
                newPath.AddSegment((ISegment)pThisLine, ref obj, ref obj);
                //The spatial reference associated with geometryCollection will be assigned to all incoming paths and segments.
                queryGeomCollection.AddGeometry(newPath as IGeometry, ref obj, ref obj);
            }

            //search for records that intersect these perpendicular geometries

            ISpatialFilter spatialFilter = (ISpatialFilter)new SpatialFilter();
            spatialFilter.WhereClause = WhereClause;
            spatialFilter.SpatialRel = esriSpatialRelEnum.esriSpatialRelIntersects;
            spatialFilter.SearchOrder = esriSearchOrder.esriSearchOrderSpatial;

            spatialFilter.Geometry = queryGeomBag;

            IFeatureCursor pFeatCursLines = null;
            try
            {
                pFeatCursLines = FeatureClass.Search(spatialFilter, false);
            }
            catch (Exception ex)
            {
                ShowError(ex.Message);
                return CurveInfoFromNeighbours;
            }

            IFeature pFeat = pFeatCursLines.NextFeature();
            while (pFeat != null)
            {
                IGeometry pFoundLineGeom = pFeat.ShapeCopy;

                //if the feature has no radius attribute, skip.
                double dRadius = pFeat.get_Value(idxRadius) is DBNull ? 0 : (double)pFeat.get_Value(idxRadius);
                if (dRadius == 0)
                {//null or zero radius so skip.
                    Marshal.ReleaseComObject(pFeat);
                    pFeat = pFeatCursLines.NextFeature();
                    continue;
                }

                int? centerpointID = pFeat.get_Value(idxCenterPointID) is DBNull ? null : (int?)pFeat.get_Value(idxCenterPointID);
                if (centerpointID == null)
                {//null centrpointID so skip.
                    Marshal.ReleaseComObject(pFeat);
                    pFeat = pFeatCursLines.NextFeature();
                    continue;
                }

                ITopologicalOperator6 pTopoOp6 = (ITopologicalOperator6)queryMultiPartPolyLine;
                IGeometry pResultGeom = pTopoOp6.IntersectEx(pFoundLineGeom, false, esriGeometryDimension.esriGeometry0Dimension);
                if (pResultGeom == null || pResultGeom.IsEmpty)
                {
                    Marshal.ReleaseComObject(pFeat);
                    pFeat = pFeatCursLines.NextFeature();
                    continue;
                }

                ISegmentCollection pFoundLineGeomSegs = pFoundLineGeom as ISegmentCollection;
                bool bHasCurves = false;
                pFoundLineGeomSegs.HasNonLinearSegments(ref bHasCurves);
                if (!bHasCurves)
                {
                    Marshal.ReleaseComObject(pFeat);
                    pFeat = pFeatCursLines.NextFeature();
                    continue;
                }

                IPointCollection5 PtColl = (IPointCollection5)pResultGeom;
                if (PtColl.PointCount > 1)
                {
                    Marshal.ReleaseComObject(pFeat);
                    pFeat = pFeatCursLines.NextFeature();
                    continue;
                }
                IPolycurve pPolyCurve4Tangent = pFoundLineGeom as IPolycurve;

                for (int j = 0; j < PtColl.PointCount; j++)
                {
                    IPoint p = PtColl.get_Point(j);
                    IPoint outPoint = (IPoint)new Point();
                    double dDistanceAlong = 0;
                    double dDistanceFromCurve = 0;
                    bool bOffsetRight = true;

                    //work out if the point is to the left or right of the original 
                    inPolycurve.QueryPointAndDistance(esriSegmentExtension.esriNoExtension, p, false, outPoint,
                      ref dDistanceAlong, ref dDistanceFromCurve, ref bOffsetRight);

                    ILine pTangent = (ILine)new Line();
                    dDistanceAlong = 0;
                    dDistanceFromCurve = 0;
                    bool bOnRight = true;

                    pPolyCurve4Tangent.QueryPointAndDistance(esriSegmentExtension.esriNoExtension, p, false, outPoint,
                      ref dDistanceAlong, ref dDistanceFromCurve, ref bOnRight);
                    pPolyCurve4Tangent.QueryTangent(esriSegmentExtension.esriNoExtension, dDistanceAlong, false, 100, pTangent);

                    //compare the tangent bearing with the normal to check for orthogonality
                    IVector3D vecTangent = (IVector3D)new Vector3D();
                    vecTangent.PolarSet(pTangent.Angle, 0, 1);

                    IVector3D vecNormal = (IVector3D)new Vector3D();
                    vecNormal.PolarSet(pNormalLine.Angle, 0, 1);

                    ILine pHitDistanceForRadiusDifference = (ILine)new Line();
                    pHitDistanceForRadiusDifference.PutCoords(pNormalLine.FromPoint, outPoint);
                    double dRadiusDiff = pHitDistanceForRadiusDifference.Length;

                    double dDotProd = vecTangent.DotProduct(vecNormal);
                    double dAngleCheck = Math.Acos(dDotProd) * 180 / Math.PI; //in degrees
                    dAngleCheck = Math.Abs(dAngleCheck - 90);

                    if (dAngleCheck < CurveByInferenceSettings.Instance.AngleToleranceTangentCompareInDegrees)
                    {
                        //work out concavity orientation with respect to the original line using the radius sign and dot product
                        dDotProd = inGeomVector.DotProduct(vecTangent);
                        double dTangentCheck = Math.Acos(dDotProd) * 180 / Math.PI; // in degrees
                        //dTangentCheck at this point should be close to 0 or 180 degrees.
                        
                        bool bIsConvex = ((dTangentCheck < 90 && dRadius < 0 && !bOffsetRight) ||
                                          (dTangentCheck > 90 && dRadius > 0 && !bOffsetRight) ||
                                          (dTangentCheck < 90 && dRadius > 0 && bOffsetRight) ||
                                          (dTangentCheck > 90 && dRadius < 0 && bOffsetRight));

                        double dUnitSignChange = 1;

                        if (!bIsConvex)
                            dUnitSignChange = -1;

                        double dDerivedRadius = (Math.Abs(dRadius)) + dRadiusDiff * dUnitSignChange;

                        dUnitSignChange = 1;
                        //now compute inferred left/right for candidate
                        if (bIsConvex && !bOffsetRight)
                            dUnitSignChange = -1;

                        if (!bIsConvex && bOffsetRight)
                            dUnitSignChange = -1;

                        dDerivedRadius = dDerivedRadius * dUnitSignChange;

                        //string sHarvestedCurveInfo = pFeat.OID.ToString() + "," + dDerivedRadius.ToString("#.000") + "," + centerpointID.ToString() + "," + dRadiusDiff.ToString("#.000");
                        CurveInfoFromNeighbours.Add(new RelatedCurve(pFeat.OID, dDerivedRadius, centerpointID.Value, RelationTypes.Parallel));
                    }
                }

                Marshal.ReleaseComObject(pFeat);
                pFeat = pFeatCursLines.NextFeature();
            }
            Marshal.FinalReleaseComObject(pFeatCursLines);

            return CurveInfoFromNeighbours;
        }

        public List<RelatedCurve> GetTangentCurveMatchFeatures(IFeatureClass FeatureClass, IPolycurve inPolycurve, string WhereClause,
                                                 double SegementLength)
            //double AngleToleranceTangentCompareInDegrees, double StraightLinesBreakLessThanInDegrees, 
            //double MaximumDeltaInDegrees, double ExcludeTangentsShorterThan,
            //                                     out int outFoundTangentCurvesCount, ref List<RelatedCurve> CurveInfoFromNeighbours)
        {
            List<RelatedCurve> CurveInfoFromNeighbours = new List<RelatedCurve>();

            ILine pOriginalChord = (ILine)new Line();
            pOriginalChord.PutCoords(inPolycurve.FromPoint, inPolycurve.ToPoint);
            IVector3D vecOriginalSelected = (IVector3D)new Vector3D();
            vecOriginalSelected.PolarSet(pOriginalChord.Angle, 0, 1);

            int idxRadius = FeatureClass.FindField("RADIUS");
            if (idxRadius == -1)
                return CurveInfoFromNeighbours;

            int idxCenterPointID = FeatureClass.FindField("CENTERPOINTID");
            if (idxCenterPointID == -1)
                return CurveInfoFromNeighbours;

            IGeometryBag pGeomBag = (IGeometryBag)new GeometryBag();
            IGeometryCollection pGeomColl = (IGeometryCollection)pGeomBag;

            IGeometry MultiPartPolyLine = (IGeometry)new Polyline(); //qi
            IGeoDataset pGeoDS = (IGeoDataset)FeatureClass;
            ISpatialReference spatialRef = pGeoDS.SpatialReference;
            MultiPartPolyLine.SpatialReference = spatialRef;

            IGeometryCollection geometryCollection2 = MultiPartPolyLine as IGeometryCollection;

            ILine inGeomTangentLineAtEnd = new Line(); //new
            ILine inGeomTangentLineAtStart = new Line(); //new
            object objMissing = Type.Missing;

            for (int i = 0; i < 2; i++)
            {
                ILine pThisLine = null;
                if (i == 0)
                {
                    //Add line tangent at end point
                    inPolycurve.QueryTangent(esriSegmentExtension.esriExtendAtTo, 1.0, true, 0.2, inGeomTangentLineAtEnd);
                    pThisLine = new Line();
                    pThisLine.PutCoords(inGeomTangentLineAtEnd.FromPoint, inGeomTangentLineAtEnd.ToPoint);
                    pGeomColl.AddGeometry(pThisLine);
                }
                else
                {
                    //Add line tangent at start point
                    inPolycurve.QueryTangent(esriSegmentExtension.esriExtendAtFrom, 0.0, true, 0.2, inGeomTangentLineAtStart);
                    pThisLine = new Line();
                    pThisLine.PutCoords(inGeomTangentLineAtStart.FromPoint, inGeomTangentLineAtStart.ToPoint);
                    pGeomColl.AddGeometry(pThisLine);
                }
                //Create a new path for each line.

                ISegmentCollection newPath = (ISegmentCollection)new Path();
                newPath.AddSegment((ISegment)pThisLine, ref objMissing, ref objMissing);
                //The spatial reference associated with geometryCollection will be assigned to all incoming paths and segments.
                geometryCollection2.AddGeometry(newPath as IGeometry, ref objMissing, ref objMissing);
            }

            //now buffer the tangent lines
            IGeometryCollection outBufferedGeometryCol = (IGeometryCollection)new GeometryBag();
            for (int jj = 0; jj < geometryCollection2.GeometryCount; jj++)
            {
                IPath pPath = geometryCollection2.get_Geometry(jj) as IPath;
                IGeometryCollection pPolyL = (IGeometryCollection)new Polyline();
                pPolyL.AddGeometry((IGeometry)pPath);

                ITopologicalOperator topologicalOperator = (ITopologicalOperator)pPolyL;
                IPolygon pBuffer = topologicalOperator.Buffer(0.1) as IPolygon;
                outBufferedGeometryCol.AddGeometry(pBuffer, ref objMissing, ref objMissing);
            }
            ITopologicalOperator pUnionedBuffers = null;
            pUnionedBuffers = new Polygon() as ITopologicalOperator;
            pUnionedBuffers.ConstructUnion((IEnumGeometry)outBufferedGeometryCol);

            ISpatialFilter pSpatFilt = new SpatialFilter();
            pSpatFilt.WhereClause = WhereClause;
            pSpatFilt.SpatialRel = esriSpatialRelEnum.esriSpatialRelIntersects;
            pSpatFilt.SearchOrder = esriSearchOrder.esriSearchOrderSpatial;

            pSpatFilt.Geometry = (IGeometry)pUnionedBuffers;

            IFeatureCursor pFeatCursLines = null;
            try
            {
                pFeatCursLines = FeatureClass.Search(pSpatFilt, false);
            }
            catch (Exception ex)
            {
                ShowError(ex.Message);
                return CurveInfoFromNeighbours;
            }
            IVector3D foundGeomVector = (IVector3D)new Vector3D();
            IFeature foundFeature = null;
            bool bHasTangentStraightLineAtJunction = false;

            //A list of relative orientation values that have large breaks
            List<RelativeOrientation> lstLargeBreak = new List<RelativeOrientation>();

            while ((foundFeature = pFeatCursLines.NextFeature()) != null)
            {
                IGeometry foundLineGeom = foundFeature.ShapeCopy;
                IPolycurve foundPolyCurve = foundLineGeom as IPolycurve;
                RelativeOrientation iRelativeOrientation = GetRelativeOrientation(foundPolyCurve, inPolycurve);
                //iRelativeOrientation == 1 --> closest points are original TO and found TO
                //iRelativeOrientation == 2 --> closest points are original TO and found FROM
                //iRelativeOrientation == 3 --> closest points are original FROM and found TO
                //iRelativeOrientation == 4 --> closest points are original FROM and found FROM

                //if the feature has no radius attribute, skip.
                double foundRadius = (foundFeature.get_Value(idxRadius) is DBNull) ? 0 : (double)foundFeature.get_Value(idxRadius);
                int? foundCentriodID = (foundFeature.get_Value(idxCenterPointID) is DBNull) ? null : (int?)foundFeature.get_Value(idxCenterPointID);

                /******************************
                 * 
                 *   Compare chord of the in geometry vs the found geometry
                 * 
                 * ****************************/


                ILine foundChord = new Line();
                foundChord.PutCoords(foundPolyCurve.FromPoint, foundPolyCurve.ToPoint);

                //Not sure about this, but it looks like this may be sensitive to the order in which the records are returned

                //first check for likelihood that subject line is supposed to stay straight, by geometry chord bearing angle break test
                //compare the in geometries chord with the found geometries chord
                foundGeomVector.PolarSet(foundChord.Angle, 0, 1);
                double chordsAngleDelta = Math.Abs(Math.Acos(foundGeomVector.DotProduct(vecOriginalSelected)) * 180 / Math.PI); //in degrees

                //large angle break non-tangent, greater than 3 degrees
                if (chordsAngleDelta > CurveByInferenceSettings.Instance.dLargeAngleBreakInDegrees && chordsAngleDelta < (180 - CurveByInferenceSettings.Instance.dLargeAngleBreakInDegrees))
                {
                    if (!lstLargeBreak.Contains(iRelativeOrientation))
                    {
                        lstLargeBreak.Add(iRelativeOrientation);

                        //check to see if there is already a related curve for the current relative orientation
                        //if(CurveInfoFromNeighbours.Any(w=>w.Orientation == iRelativeOrientation))
                        //{
                        //    bHasTangentStraightLineAtJunction = true;
                        //}
                    }

                }

                double ExcludeTangentsShorterThan = SegementLength * CurveByInferenceSettings.Instance.ExcludeTangentsShorterThanScaler;
                
                /*
                if(foundPolyCurve.Length < ExcludeTangentsShorterThan)
                {
                    //exclude short segements
                    Marshal.ReleaseComObject(foundFeature);
                    continue;
                }
                else if (!foundCentriodID.HasValue && foundRadius == 0)
                {
                    //attribute straight line, check the geometry to see if it is tangent at the endpoint
                    if (chordsAngleDelta <= CurveByInferenceSettings.Instance.StraightLinesBreakLessThanInDegrees || (180 - chordsAngleDelta) < CurveByInferenceSettings.Instance.StraightLinesBreakLessThanInDegrees)
                    {
                        if (lstLargeBreak.Contains(iRelativeOrientation))
                            bHasTangentStraightLineAtJunction = true;
                    }
                }
                else if (foundCentriodID.HasValue && foundRadius != 0)
                {
                    //attribute curved line
                    if ((inPolycurve.Length / foundRadius * 180 / Math.PI) > CurveByInferenceSettings.Instance.MaximumDeltaInDegrees)
                    {
                        //if the resulting curve ('in feature' length with 'found feature' radius) would have a central angle more than MaximumDeltaInDegrees degrees then skip
                        Marshal.ReleaseComObject(foundFeature);
                        continue;
                    }
                }
                else
                {
                    //invalid state, curves should have both centriod and radius, straigh lines should have niether.
                    Marshal.ReleaseComObject(foundFeature);
                    throw new Exception(string.Format("Invalid attribute state")); 
                }
                 */

                if ((chordsAngleDelta <= CurveByInferenceSettings.Instance.StraightLinesBreakLessThanInDegrees || (180 - chordsAngleDelta) < CurveByInferenceSettings.Instance.StraightLinesBreakLessThanInDegrees)
                    && !foundCentriodID.HasValue && foundRadius == 0 && !(foundPolyCurve.Length < ExcludeTangentsShorterThan))
                {
                    if (lstLargeBreak.Contains(iRelativeOrientation))
                        bHasTangentStraightLineAtJunction = true;
                }

                if (!foundCentriodID.HasValue || foundRadius == 0 || foundPolyCurve.Length < ExcludeTangentsShorterThan)
                {
                    //if the feature has a null centrpointID then skip.
                    Marshal.ReleaseComObject(foundFeature);
                    continue;
                }

                if (Math.Abs(inPolycurve.Length / foundRadius * 180 / Math.PI) > CurveByInferenceSettings.Instance.MaximumDeltaInDegrees)
                {
                    //if the resulting curve ('in feature' length with 'found feature' radius) would have a central angle more than MaximumDeltaInDegrees degrees then skip
                    Marshal.ReleaseComObject(foundFeature);
                    continue;
                }

                /******************************
                 * 
                 *   Compare two lines, one from each in geometry and found geometry taken at the closes ends of the lines
                 *   Ie, compare the slopes of the two lines where they touch (or at least where they closest).
                 * 
                 * ****************************/

                //if geometry of curve neighbor curves have been cracked then there can be more than one segment
                //however since all segments would be circular arcs, just need to test the first segment
                ISegmentCollection pFoundLineGeomSegs = foundLineGeom as ISegmentCollection;
                ISegment pSeg = pFoundLineGeomSegs.get_Segment(0);
                if (!(pSeg is ICircularArc))
                {
                    Marshal.ReleaseComObject(foundFeature);
                    continue;
                }

                IVector3D vect1 = (IVector3D)new Vector3D();
                IVector3D vect2 = (IVector3D)new Vector3D();
                ILine tang = new Line();
                double dUnitSignChange = 1;
                if (iRelativeOrientation == RelativeOrientation.To_To) //closest points are original TO and found TO
                {
                    dUnitSignChange = -1;
                    vect1.PolarSet(inGeomTangentLineAtEnd.Angle, 0, 1);
                    foundPolyCurve.QueryTangent(esriSegmentExtension.esriExtendAtTo, 1.0, true, 1, tang);
                    vect2.PolarSet(tang.Angle, 0, 1);
                }
                else if (iRelativeOrientation == RelativeOrientation.To_From)//closest points are original TO and found FROM
                {
                    vect1.PolarSet(inGeomTangentLineAtEnd.Angle, 0, 1);
                    foundPolyCurve.QueryTangent(esriSegmentExtension.esriExtendAtFrom, 0.0, true, 1, tang);
                    vect2.PolarSet(tang.Angle, 0, 1);
                }
                else if (iRelativeOrientation == RelativeOrientation.From_To)//closest points are original FROM and found TO
                {
                    vect1.PolarSet(inGeomTangentLineAtStart.Angle, 0, 1);
                    foundPolyCurve.QueryTangent(esriSegmentExtension.esriExtendAtTo, 1.0, true, 1, tang);
                    vect2.PolarSet(tang.Angle, 0, 1);
                }
                else if (iRelativeOrientation == RelativeOrientation.From_From)//closest points are original FROM and found FROM
                {
                    dUnitSignChange = -1;
                    vect1.PolarSet(inGeomTangentLineAtStart.Angle, 0, 1);
                    foundPolyCurve.QueryTangent(esriSegmentExtension.esriExtendAtFrom, 0.0, true, 1, tang);
                    vect2.PolarSet(tang.Angle, 0, 1);
                }

                double tangentAngleDelta = Math.Abs(Math.Acos(vect1.DotProduct(vect2)) * 180 / Math.PI); //in degrees
                if (tangentAngleDelta < CurveByInferenceSettings.Instance.AngleToleranceTangentCompareInDegrees || (180 - tangentAngleDelta) < CurveByInferenceSettings.Instance.AngleToleranceTangentCompareInDegrees)
                {

                    double inferredRadius = foundRadius * dUnitSignChange;

                    //string sHarvestedCurveInfo = foundFeature.OID.ToString() + "," + inferredRadius.ToString("#.000") + "," + foundCentriodID.ToString() + "," + "t";
                    CurveInfoFromNeighbours.Add(new RelatedCurve(foundFeature.OID, inferredRadius, foundCentriodID.Value, RelationTypes.Tangent) { Orientation = iRelativeOrientation });
                }
                Marshal.ReleaseComObject(foundFeature);
            }
            Marshal.FinalReleaseComObject(pFeatCursLines);

            if (bHasTangentStraightLineAtJunction)
                CurveInfoFromNeighbours.Clear(); //open to logic change to be less conservative

            //Original source didn't clear the list before, returing all the found records by reference.
            //calling procedure didn't do anything with those features though, only consumed the features is true was returned

            return CurveInfoFromNeighbours;
        }

        public enum RelativeOrientation { To_To = 1, To_From = 2, From_To = 3, From_From = 4 }
        private RelativeOrientation GetRelativeOrientation(IPolycurve pFoundLineAsPolyCurve, IPolycurve inPolycurve)
        {
            //iRelativeOrientation == 1 --> closest points are original TO and found TO
            //iRelativeOrientation == 2 --> closest points are original TO and found FROM
            //iRelativeOrientation == 3 --> closest points are original FROM and found TO
            //iRelativeOrientation == 4 --> closest points are original FROM and found FROM

            Dictionary<int, double> dictSort2GetShortest = new Dictionary<int, double>();

            ILine pFoundTo_2_OriginalTo = new Line();
            pFoundTo_2_OriginalTo.PutCoords(pFoundLineAsPolyCurve.ToPoint, inPolycurve.ToPoint);
            dictSort2GetShortest.Add(1, pFoundTo_2_OriginalTo.Length);

            ILine pFoundFrom_2_OriginalTo = new Line();
            pFoundFrom_2_OriginalTo.PutCoords(pFoundLineAsPolyCurve.FromPoint, inPolycurve.ToPoint);
            dictSort2GetShortest.Add(2, pFoundFrom_2_OriginalTo.Length);

            ILine pFoundTo_2_OriginalFrom = new Line();
            pFoundTo_2_OriginalFrom.PutCoords(pFoundLineAsPolyCurve.ToPoint, inPolycurve.FromPoint);
            dictSort2GetShortest.Add(3, pFoundTo_2_OriginalFrom.Length);

            ILine pFoundFrom_2_OriginalFrom = new Line();
            pFoundFrom_2_OriginalFrom.PutCoords(pFoundLineAsPolyCurve.FromPoint, inPolycurve.FromPoint);
            dictSort2GetShortest.Add(4, pFoundFrom_2_OriginalFrom.Length);

            var sortedDict = from entry in dictSort2GetShortest orderby entry.Value ascending select entry;
            var pEnum = sortedDict.GetEnumerator();
            pEnum.MoveNext(); //get the first key for the shortest line
            var pair = pEnum.Current;
            int iOpt = pair.Key;
            return (RelativeOrientation)iOpt;
        }

        #endregion

        #region UI Update

        void setStepProgressorProperties(int MaxRange, String Message, int MinRange = 0, int Position = 0, int StepValue = 1)
        {
            if (stepProgress != null)
            {
                stepProgress.Hide();
                Marshal.ReleaseComObject(stepProgress);
            }

            stepProgress = progressDialogFactory.Create(cancelTracker, 0);
            IProgressDialog2 progressDialog = (IProgressDialog2)stepProgress;
            progressDialog.Animation = esriProgressAnimationTypes.esriProgressSpiral;
            progressDialog.Title = "Inferring Curved Segments";

            stepProgress.Message = Message;
            if (MaxRange != MinRange)  //for some reason, if you set the values the same, the progressor doesn't go away
            {
                stepProgress.MinRange = MinRange;
                stepProgress.MaxRange = MaxRange;
                stepProgress.StepValue = StepValue;
            }
            stepProgress.Show();
        }

        public event PropertyChangedEventHandler PropertyChanged;
        private void RaisePropertyChanged(params string[] properties)
        {
            if (PropertyChanged != null)
                foreach (string property in properties)
                    PropertyChanged(this, new PropertyChangedEventArgs(property));

        }

        public void ShowError(string message)
        {
            MessageBox.Show(message);
        }
        #endregion
    }
}