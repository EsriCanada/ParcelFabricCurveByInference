# Parcel Fabric Curve By Inference

This Add-In will first identify straight lines in the parcel fabric that could potentially be an arc, and second update those arcs. Some of the inferred arcs will be updated automatically, but in some cases, there will not be enough information from the surrounding data to populate the radius and centre point information automatically. In those cases, the parcel fabric operator can manually enter the radius and centre point information via the Add-In in order to update those records. 

#Instructions
Once the Add-In has been compiled and installed, use the following steps to configure the Add-In for use in ArcMap:

1.	Open ArcMap and navigate to the ‘Customize’ menu> Customize Mode> Commands Tab
2.	In the ‘Show Commands Containing’ entry box, type the word ‘Curve’
3.	Under the Categories section, select ‘Parcel Fabric Add-Ins’
4.	Under the Commands section, drag the ‘Show Curve By Inference’ blue circle onto the Parcel Editor toolbar

![configure](https://cloud.githubusercontent.com/assets/8808482/16622889/ba0b4b14-4369-11e6-9bdf-a69aedd2d3db.jpg)

Once the Curve By Inference Add-In has been added to the ArcMap interface, the steps below provide information on how to use the Add-In:

1.	Open ArcMap and make sure your parcel fabric is loaded into your map session
2.	Navigate to the Parcel Editor drop down> Start Editing
3.	Select the parcels you would like to investigate
4.	Click on the Blue Circle icon to launch the Add-In interface 
5.	Dock the Curve by Inference window anywhere of your choosing
6.	Click on ‘Find Rows’ – the Add-In will populate the dialog box with all of the segments it identifies that could potentially be arcs
7.	The Add-In reports the total number of records that could potentially be arcs, as well as the number of records that can be updated automatically

 ![window](https://cloud.githubusercontent.com/assets/8808482/16622659/e8beb992-4368-11e6-88e7-fa58b26f8056.jpg)
 
8.	You can sort on the ‘Action’ field to review all the ‘Update’ rows in order
9.	If you wish to investigate some of the rows to be updated automatically prior to updating them, you can right click on each row to access the various options (flash, select, pan, zoom)
10.	When clicking on a record, the Add-In presents you with additional details of the related records
11.	When you are satisfied with the results that will be populated automatically, click on ‘Update Rows’
  * Note: After the Add-In updates the records, the interface is cleared of all records – this is done on purpose. The Add-In is an iterative process, meaning that after some records are updated, additional segments may be identified, and other segments that were initially reported but not updated automatically, may now be reported and updated automatically. 
12.	Click on ‘Find Rows’ again to find the remaining zero radius arcs
13.	If the Add-In reported any additional rows that can be updated automatically, choose to ‘Update Rows’ – continue this process until the only records that are reported by the Add-In need to be updated manually
14.	Right click on the first record in the Curve By Inference table that needs to be updated manually> Zoom to
15.	Use the survey plan to populate the segment with the surveyed radius value 
16.	Populate the centre point value from the related record information reported by the Add-In
17.	Additionally, you’ll know what orientation to give the curve (+/-) based on the radius values reported in the related record information section of the Add-In

 ![manual](https://cloud.githubusercontent.com/assets/8808482/16622657/e77b74da-4368-11e6-9627-319810f19c36.jpg)
 
18.	Once the radius and centre point information is populated, the ‘Action’ drop down will automatically change to a value of “Update”
19.	You can right click on the record and ‘Apply Change’ to update the row, or you can populate several records and then click the ‘Update Rows’ button
20.	Continue with this process until all segments or updated, or the only records left reported in the dialog box are false positives and should remain a straight line

Note: You will need to save edits in order to save the work done within the edit session. 

# Known Issues

If you encounter any other issues with the Add-In, please send a note to Sarah: ssibbett@esri.ca. The Add-In is delivered as-is, but we will do our best to investigate problems that have been reported. 

# Requirements

This Add-In requires ArcGIS Desktop 10.1 or higher, with a Standard or Advanced license. 

# Compiling Source Code

The Add-In source code will compile with:
  * ArcGIS 10.1 SDK or higher
  * .Net 3.5 or higher
  * Visual Studio 2010 or higher

## License

You may not use this file except in compliance with the license agreement.

Unless required by applicable law or agreed to in writing, Licensor provides the Work (and each Contributor provides its Contributions) on an "AS IS" BASIS, WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied, including, without limitation, any warranties or conditions of TITLE, NON-INFRINGEMENT, MERCHANTABILITY, or FITNESS FOR A PARTICULAR PURPOSE. Licensee is solely responsible for determining the appropriateness of using or redistributing the Work and assume any risks associated with Licensee’s exercise of permissions under this License.

A copy of the license is available in the repository's [license.txt](license.txt) file.
